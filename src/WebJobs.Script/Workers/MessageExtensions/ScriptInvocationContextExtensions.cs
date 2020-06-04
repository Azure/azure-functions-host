// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal static class ScriptInvocationContextExtensions
    {
        public static async Task<InvocationRequest> ToRpcInvocationRequest(this ScriptInvocationContext context, ILogger logger, Capabilities capabilities)
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
                if (!rpcValueCache.TryGetValue(input.val, out TypedData rpcValue))
                {
                    rpcValue = await input.val.ToRpc(logger, capabilities);
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
                    rpcValue = await pair.Value.ToRpc(logger, capabilities);
                    rpcValueCache.Add(pair.Value, rpcValue);
                }

                invocationRequest.TriggerMetadata.Add(pair.Key, rpcValue);
            }

            return invocationRequest;
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
            // of http binding metadata, we'll skip those
            if (context.FunctionMetadata.IsHttpInAndOutFunction() && excludeHttpTriggerMetadata)
            {
                if (bindingData.Value is HttpRequest)
                {
                    // will exclude req/$request binding data members
                    return true;
                }

                if (bindingData.Key.Equals("headers", StringComparison.OrdinalIgnoreCase) || bindingData.Key.Equals("query", StringComparison.OrdinalIgnoreCase))
                {
                    // these values are already part of the the request
                    return true;
                }
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

        public static async Task<HttpScriptInvocationContext> ToHttpScriptInvocationContext(this ScriptInvocationContext scriptInvocationContext)
        {
            HttpScriptInvocationContext httpScriptInvocationContext = new HttpScriptInvocationContext();

            // populate metadata
            foreach (var bindingDataPair in scriptInvocationContext.BindingData)
            {
                if (bindingDataPair.Value != null)
                {
                    if (bindingDataPair.Value is HttpRequest)
                    {
                        // no metadata for httpTrigger
                        continue;
                    }
                    if (bindingDataPair.Key.EndsWith("trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        // Data will include value of the trigger. Do not duplicate
                        continue;
                    }
                    httpScriptInvocationContext.Metadata[bindingDataPair.Key] = GetHttpScriptInvocationContextValue(bindingDataPair.Value);
                }
            }

            // populate input bindings
            foreach (var input in scriptInvocationContext.Inputs)
            {
                if (input.val is HttpRequest httpRequest)
                {
                    httpScriptInvocationContext.Data[input.name] = await httpRequest.GetRequestAsJObject();
                    continue;
                }
                httpScriptInvocationContext.Data[input.name] = GetHttpScriptInvocationContextValue(input.val, input.type);
            }

            return httpScriptInvocationContext;
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

        internal static object GetHttpScriptInvocationContextValue(object inputValue, DataType dataType = DataType.String)
        {
            if (inputValue is byte[] byteArray)
            {
                if (dataType == DataType.Binary)
                {
                    return byteArray;
                }
                return Convert.ToBase64String(byteArray);
            }
            try
            {
                return JObject.FromObject(inputValue);
            }
            catch
            {
            }
            return JsonConvert.SerializeObject(inputValue);
        }
    }
}