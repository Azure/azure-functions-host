// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.TypedData.DataOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal static class ScriptInvocationContextExtensions
    {
        public static async Task<InvocationRequest> ToRpcInvocationRequest(this ScriptInvocationContext context, ILogger logger, GrpcCapabilities capabilities, SharedMemoryManager sharedMemoryManager = null)
        {
            bool excludeHttpTriggerMetadata = !string.IsNullOrEmpty(capabilities.GetCapabilityState(RpcWorkerConstants.RpcHttpTriggerMetadataRemoved));

            var invocationRequest = new InvocationRequest
            {
                FunctionId = context.FunctionMetadata.GetFunctionId(),
                InvocationId = context.ExecutionContext.InvocationId.ToString(),
                TraceContext = GetRpcTraceContext(context.Traceparent, context.Tracestate, context.Attributes, logger),
            };

            var rpcValueCache = new Dictionary<object, TypedData>();

            foreach (var input in context.Inputs)
            {
                TypedData rpcValue = null;
                if (input.val == null || !rpcValueCache.TryGetValue(input.val, out rpcValue))
                {
                    // TODO gochaudh: Check config before using SharedMemoryManager
                    rpcValue = await input.val.ToRpc(logger, capabilities, sharedMemoryManager);
                    if (input.val != null)
                    {
                        if (rpcValue.DataCase == RpcDataType.SharedMemoryData && sharedMemoryManager != null)
                        {
                            // Add the name of the MemoryMappedFile created for this input to the list of
                            // SharedMemoryResources used for this invocation
                            context.SharedMemoryResources.Add(rpcValue.SharedMemoryData.MemoryMappedFileName);
                        }
                        rpcValueCache.Add(input.val, rpcValue);
                    }
                }

                var parameterBinding = new ParameterBinding
                {
                    Name = input.name,
                    Data = rpcValue
                };
                invocationRequest.InputData.Add(parameterBinding);
            }

            foreach (var pair in context.BindingData)
            {
                if (ShouldSkipBindingData(pair, context, excludeHttpTriggerMetadata))
                {
                    continue;
                }

                if (!rpcValueCache.TryGetValue(pair.Value, out TypedData rpcValue))
                {
                    // TODO gochaudh: Check config before using SharedMemoryManager
                    rpcValue = await pair.Value.ToRpc(logger, capabilities, sharedMemoryManager);
                    rpcValueCache.Add(pair.Value, rpcValue);
                }

                if (rpcValue.DataCase == RpcDataType.SharedMemoryData && sharedMemoryManager != null)
                {
                    // Add the name of the MemoryMappedFile created for this input to the list of
                    // SharedMemoryResources used for this invocation
                    context.SharedMemoryResources.Add(rpcValue.SharedMemoryData.MemoryMappedFileName);
                }
                invocationRequest.TriggerMetadata.Add(pair.Key, rpcValue);
            }

            return invocationRequest;
        }

        public static bool FreeSharedMemoryResources(this ScriptInvocationContext context, SharedMemoryManager sharedMemoryManager)
        {
            foreach (string mapName in context.SharedMemoryResources)
            {
                if (!sharedMemoryManager.TryFree(mapName))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determine whether we can omit the specified binding data for performance.
        /// </summary>
        private static bool ShouldSkipBindingData(KeyValuePair<string, object> bindingData, ScriptInvocationContext context, bool excludeHttpTriggerMetadata)
        {
            if (bindingData.Value == null)
            {
                return true;
            }

            // if this is an http request and the worker declares that it handles exclusion
            // of req/$request binding data members
            if (excludeHttpTriggerMetadata && bindingData.Value is HttpRequest)
            {
                return true;
            }

            if (bindingData.Key.Equals("sys", StringComparison.OrdinalIgnoreCase) &&
                bindingData.Value.GetType().Name.Equals("SystemBindingData", StringComparison.OrdinalIgnoreCase))
            {
                // The system binding data isn't RPC friendly. It's designed for in memory use in the binding
                // pipeline (e.g. sys.RandGuid, etc.)
                return true;
            }

            return false;
        }

        internal static RpcTraceContext GetRpcTraceContext(string activityId, string traceStateString, IEnumerable<KeyValuePair<string, string>> tags, ILogger logger)
        {
            RpcTraceContext traceContext = new RpcTraceContext
            {
                TraceParent = activityId ?? string.Empty,
                TraceState = traceStateString ?? string.Empty,
            };

            foreach (KeyValuePair<string, string> tag in tags ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrEmpty(tag.Value))
                {
                    logger?.LogDebug($"Excluding {tag.Key} from being added to TraceContext.Attributes since it's value is null/empty");
                }
                else
                {
                    if (traceContext.Attributes.ContainsKey(tag.Key))
                    {
                        logger?.LogWarning($"Overwriting '{tag.Key}' with existing value '{traceContext.Attributes[tag.Key]}' with '{tag.Value}'");
                    }
                    traceContext.Attributes[tag.Key] = tag.Value;
                }
            }

            return traceContext;
        }
    }
}
