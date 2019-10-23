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
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    internal static class ScriptInvocationContextExtensions
    {
        public static InvocationRequest ToRpcInvocationRequest(this ScriptInvocationContext context, bool isTriggerMetadataPopulatedByWorker, ILogger logger, Capabilities capabilities)
        {
            InvocationRequest invocationRequest = new InvocationRequest()
            {
                FunctionId = context.FunctionMetadata.FunctionId,
                InvocationId = context.ExecutionContext.InvocationId.ToString(),
                TraceContext = GetRpcTraceContext(context.Traceparent, context.Tracestate, context.Attributes, logger),
            };

            foreach (var pair in context.BindingData)
            {
                if (pair.Value != null)
                {
                    if ((pair.Value is HttpRequest) && isTriggerMetadataPopulatedByWorker)
                    {
                        continue;
                    }
                    invocationRequest.TriggerMetadata.Add(pair.Key, pair.Value.ToRpc(logger, capabilities));
                }
            }
            foreach (var input in context.Inputs)
            {
                invocationRequest.InputData.Add(new ParameterBinding()
                {
                    Name = input.name,
                    Data = input.val.ToRpc(logger, capabilities)
                });
            }

            return invocationRequest;
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
            return JsonConvert.SerializeObject(inputValue);
        }
    }
}