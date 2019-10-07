// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
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
                    traceContext.Attributes.Add(tag.Key, tag.Value);
                }
            }

            return traceContext;
        }
    }
}