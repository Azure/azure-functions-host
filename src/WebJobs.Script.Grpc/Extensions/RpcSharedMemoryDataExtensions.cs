// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Extensions
{
    internal static class RpcSharedMemoryDataExtensions
    {
        internal static async Task<RpcSharedMemory> ToRpcSharedMemoryAsync(this object value, ILogger logger, string invocationId, ISharedMemoryManager sharedMemoryManager, IFunctionDataCache functionDataCache)
        {
            if (value == null)
            {
                return new RpcSharedMemory();
            }

            if (!sharedMemoryManager.IsSupported(value))
            {
                return null;
            }

            SharedMemoryMetadata sharedMemoryMeta;

            if (value is SharedMemoryMetadata)
            {
                // Content was already present in shared memory (cache hit)
                logger.LogTrace("Object already present in shared memory for invocation id: {Id}", invocationId);
                sharedMemoryMeta = value as SharedMemoryMetadata;
            }
            else
            {
                // Put the content into shared memory and get the name of the shared memory map written to
                sharedMemoryMeta = await sharedMemoryManager.PutObjectAsync(value);
                if (sharedMemoryMeta == null)
                {
                    logger.LogTrace("Cannot write to shared memory for invocation id: {Id}", invocationId);
                    return null;
                }

                if (value is CacheableObjectStream)
                {
                    CacheableObjectStream cachableObj = value as CacheableObjectStream;
                    FunctionDataCacheKey cacheKey = cachableObj.CacheKey;

                    if (functionDataCache.TryPut(cacheKey, sharedMemoryMeta, isIncrementActiveReference: true))
                    {
                        logger.LogTrace("Put object: {CacheKey} in cache with metadata: {SharedMemoryMetadata} for invocation id: {Id}", cacheKey, sharedMemoryMeta, invocationId);
                    }
                    else
                    {
                        logger.LogTrace("Cannot put object: {CacheKey} in cache with metadata: {SharedMemoryMetadata} for invocation id: {Id}", cacheKey, sharedMemoryMeta, invocationId);
                    }
                }
            }

            RpcDataType? dataType = GetRpcDataType(value);
            if (!dataType.HasValue)
            {
                logger.LogTrace("Cannot get shared memory data type for invocation id: {Id}", invocationId);
                return null;
            }

            // TODO: If we are adding things, we need to remove from this too (and right now we are not, since we are not freeing things)
            // If written to shared memory successfully, add this shared memory map to the list of maps for this invocation
            sharedMemoryManager.AddSharedMemoryMapForInvocation(invocationId, sharedMemoryMeta.MemoryMapName);

            // Generate a response
            RpcSharedMemory sharedMem = new RpcSharedMemory()
            {
                Name = sharedMemoryMeta.MemoryMapName,
                Offset = 0,
                Count = sharedMemoryMeta.Count,
                Type = dataType.Value
            };

            logger.LogTrace("Put object in shared memory for invocation id: {Id}", invocationId);
            return sharedMem;
        }

        internal static async Task<object> ToObjectAsync(this RpcSharedMemory sharedMem, ILogger logger, string invocationId, ISharedMemoryManager sharedMemoryManager)
        {
            // Data was transferred by the worker using shared memory
            string mapName = sharedMem.Name;
            int offset = (int)sharedMem.Offset;
            int count = (int)sharedMem.Count;
            logger.LogTrace("Shared memory data transfer for invocation id: {Id} with shared memory map name: {MapName} and size: {Size} bytes", invocationId, mapName, count);

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

        private static RpcDataType? GetRpcDataType(object value)
        {
            if (value is byte[])
            {
                return RpcDataType.Bytes;
            }
            else if (value is CacheableObjectStream)
            {
                return RpcDataType.Bytes;
            }
            else if (value is SharedMemoryMetadata)
            {
                return RpcDataType.Bytes;
            }
            else if (value is string)
            {
                return RpcDataType.String;
            }

            return null;
        }
    }
}
