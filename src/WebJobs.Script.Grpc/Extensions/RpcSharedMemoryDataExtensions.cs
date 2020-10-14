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
        internal static async Task<RpcSharedMemory> ToRpcSharedMemoryAsync(this object value, ILogger logger, string invocationId, ISharedMemoryManager sharedMemoryManager)
        {
            if (!sharedMemoryManager.IsSupported(value))
            {
                return null;
            }

            // Put the content into shared memory and get the name of the shared memory map written to
            SharedMemoryMetadata putResponse = await sharedMemoryManager.PutObjectAsync(value);
            if (putResponse == null)
            {
                logger.LogTrace("Cannot write to shared memory for invocation id: {Id}", invocationId);
                return null;
            }

            // If written to shared memory successfully, add this shared memory map to the list of maps for this invocation
            sharedMemoryManager.AddSharedMemoryMapForInvocation(invocationId, putResponse.Name);

            RpcDataType? dataType = GetRpcDataType(value);
            if (!dataType.HasValue)
            {
                logger.LogTrace("Cannot get shared memory data type for invocation id: {Id}", invocationId);
                return null;
            }

            // Generate a response
            RpcSharedMemory sharedMem = new RpcSharedMemory()
            {
                Name = putResponse.Name,
                Offset = 0,
                Count = putResponse.Count,
                Type = dataType.Value
            };

            logger.LogDebug("Put object in shared memory for invocation id: {Id}", invocationId);
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
            else if (value is string)
            {
                return RpcDataType.String;
            }

            return null;
        }
    }
}
