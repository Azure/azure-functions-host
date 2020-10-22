// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Extensions
{
    internal static class RpcSharedMemoryDataExtensions
    {
        internal static async Task<RpcSharedMemory> ToRpcSharedMemory(this object value, ILogger logger, string invocationId, ISharedMemoryManager sharedMemoryManager)
        {
            if (sharedMemoryManager.IsSupported(value))
            {
                // Put the content into shared memory and get the name of the shared memory map written to
                SharedMemoryMetadata putResponse = await sharedMemoryManager.PutObjectAsync(value);
                if (putResponse != null)
                {
                    // If written to shared memory successfully, add this shared memory map to the list of maps for this invocation
                    sharedMemoryManager.AddSharedMemoryMapForInvocation(invocationId, putResponse.Name);

                    RpcSharedMemoryDataType? dataType = GetRpcSharedMemoryDataType(value);
                    if (dataType.HasValue)
                    {
                        // Generate a response
                        RpcSharedMemory sharedMem = new RpcSharedMemory()
                        {
                            Name = putResponse.Name,
                            Offset = 0,
                            Count = putResponse.Count,
                            Type = dataType.Value
                        };

                        return sharedMem;
                    }
                }
                else
                {
                    logger.LogError($"Cannot write to shared memory for invocation: {invocationId}");
                }
            }

            return null;
        }

        private static RpcSharedMemoryDataType? GetRpcSharedMemoryDataType(object value)
        {
            if (value is byte[])
            {
                return RpcSharedMemoryDataType.Bytes;
            }
            else
            {
                return null;
            }
        }
    }
}
