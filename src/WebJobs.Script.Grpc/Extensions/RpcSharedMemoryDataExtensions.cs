// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Extensions
{
    internal static class RpcSharedMemoryDataExtensions
    {
        internal static async Task<RpcSharedMemory> ToRpcSharedMemoryAsync(this object value, DataType dataType, ILogger logger, string invocationId, ISharedMemoryManager sharedMemoryManager)
        {
            if (value == null)
            {
                return new RpcSharedMemory();
            }

            if (!sharedMemoryManager.IsSupported(value))
            {
                return null;
            }

            SharedMemoryMetadata sharedMemoryMetadata;
            bool needToFreeAfterInvocation = true;

            // Check if the cache is being used or not.
            // The binding extension will only hand out ICacheAwareReadObject if the FunctionDataCache is available and enabled.
            if (value is ICacheAwareReadObject obj)
            {
                if (obj.IsCacheHit)
                {
                    // Content was already present in shared memory (cache hit)
                    logger.LogTrace("Object already present in shared memory for invocation id: {Id}", invocationId);
                    sharedMemoryMetadata = obj.CacheObject;
                    needToFreeAfterInvocation = false;
                }
                else
                {
                    // Put the content into shared memory and get the name of the shared memory map written to.
                    // This will make the SharedMemoryManager keep an active reference to the memory map.
                    sharedMemoryMetadata = await sharedMemoryManager.PutObjectAsync(obj.BlobStream);
                    if (sharedMemoryMetadata != null)
                    {
                        FunctionDataCacheKey cacheKey = obj.CacheKey;

                        // Try to add the object into the cache and keep an active ref-count for it so that it does not get
                        // evicted while it is still being used by the invocation.
                        if (obj.TryPutToCache(sharedMemoryMetadata, isIncrementActiveReference: true))
                        {
                            logger.LogTrace("Put object: {CacheKey} in cache with metadata: {SharedMemoryMetadata} for invocation id: {Id}", cacheKey, sharedMemoryMetadata, invocationId);
                            // We don't need to free the object after the invocation; it will be freed as part of the cache's
                            // eviction policy.
                            needToFreeAfterInvocation = false;
                        }
                        else
                        {
                            logger.LogTrace("Cannot put object: {CacheKey} in cache with metadata: {SharedMemoryMetadata} for invocation id: {Id}", cacheKey, sharedMemoryMetadata, invocationId);
                            // Since we could not add this object to the cache (and therefore the cache will not be able to evict
                            // it as part of its eviction policy) we will need to free it after the invocation is done.
                            needToFreeAfterInvocation = true;
                        }
                    }
                }
            }
            else
            {
                // Put the content into shared memory and get the name of the shared memory map written to
                sharedMemoryMetadata = await sharedMemoryManager.PutObjectAsync(value);
                needToFreeAfterInvocation = true;
            }

            // Check if the object was either already in shared memory or written to shared memory
            if (sharedMemoryMetadata == null)
            {
                logger.LogTrace("Cannot write to shared memory for invocation id: {Id}", invocationId);
                return null;
            }

            RpcDataType? rpcDataType = GetRpcDataType(dataType);
            if (!rpcDataType.HasValue)
            {
                logger.LogTrace("Cannot get shared memory data type for invocation id: {Id}", invocationId);
                return null;
            }

            // When using the cache, we don't need to free the memory map after using it;
            // it will be freed as per the eviction policy of the cache.
            // However, if either the cache was not enabled or the object could not be added to the cache,
            // we will need to free it after the invocation.
            if (needToFreeAfterInvocation)
            {
                // If written to shared memory successfully, add this shared memory map to the list of maps for this invocation
                // so that once the invocation is over, the memory map's resources can be freed.
                sharedMemoryManager.AddSharedMemoryMapForInvocation(invocationId, sharedMemoryMetadata.MemoryMapName);
            }

            // Generate a response
            RpcSharedMemory sharedMem = new RpcSharedMemory()
            {
                Name = sharedMemoryMetadata.MemoryMapName,
                Offset = 0,
                Count = sharedMemoryMetadata.Count,
                Type = rpcDataType.Value
            };

            logger.LogTrace("Put object in shared memory for invocation id: {Id}", invocationId);
            return sharedMem;
        }

        internal static async Task<object> ToObjectAsync(this RpcSharedMemory sharedMem, ILogger logger, string invocationId, ISharedMemoryManager sharedMemoryManager, bool isFunctionDataCacheEnabled)
        {
            // Data was transferred by the worker using shared memory
            string mapName = sharedMem.Name;
            int offset = (int)sharedMem.Offset;
            int count = (int)sharedMem.Count;
            logger.LogTrace("Shared memory data transfer for invocation id: {Id} with shared memory map name: {MapName} and size: {Size} bytes", invocationId, mapName, count);

            // If cache is enabled, we hold a reference (in the host) to the memory map created by the worker
            // so that the worker can be asked to drop its reference.
            // We will later add this object into the cache and then the memory map will be freed as per the eviction logic of the cache.
            if (isFunctionDataCacheEnabled)
            {
                switch (sharedMem.Type)
                {
                    case RpcDataType.Bytes:
                    case RpcDataType.String:
                        // This is where the SharedMemoryManager will hold a reference to the memory map
                        if (sharedMemoryManager.TryTrackSharedMemoryMap(mapName))
                        {
                            return await sharedMemoryManager.GetObjectAsync(mapName, offset, count, typeof(SharedMemoryObject));
                        }
                        break;
                    default:
                        logger.LogError("Unsupported shared memory data type: {SharedMemDataType} with FunctionDataCache for invocation id: {Id}", sharedMem.Type, invocationId);
                        throw new InvalidDataException($"Unsupported shared memory data type with FunctionDataCache: {sharedMem.Type}");
                }
            }

            // If the cache is not used, we copy the object content from the memory map so that the worker
            // can be asked to drop the reference to the memory map.
            switch (sharedMem.Type)
            {
                case RpcDataType.Bytes:
                    return await sharedMemoryManager.GetObjectAsync(mapName, offset, count, typeof(byte[]));
                case RpcDataType.String:
                    return await sharedMemoryManager.GetObjectAsync(mapName, offset, count, typeof(string));
                default:
                    logger.LogError("Unsupported shared memory data type: {SharedMemDataType} for invocation id: {Id}", sharedMem.Type, invocationId);
                    throw new InvalidDataException($"Unsupported shared memory data type: {sharedMem.Type}");
            }
        }

        private static RpcDataType? GetRpcDataType(DataType dataType)
        {
            switch (dataType)
            {
                case DataType.String:
                    return RpcDataType.String;
                case DataType.Binary:
                    return RpcDataType.Bytes;
                case DataType.Stream:
                    return RpcDataType.Bytes;
                default:
                    return null;
            }
        }
    }
}
