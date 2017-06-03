// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Rpc.Messages.TypedData.Types.Type;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class RpcMessageConversionExtensions
    {
        public static RpcFunctionMetadata ToRpcFunctionMetadata(this FunctionMetadata functionMetadata)
        {
            RpcFunctionMetadata rpcFunctionMetadata = new RpcFunctionMetadata();
            rpcFunctionMetadata.Name = functionMetadata.Name ?? string.Empty;
            rpcFunctionMetadata.Directory = functionMetadata.FunctionDirectory ?? string.Empty;
            rpcFunctionMetadata.ScriptFile = functionMetadata.ScriptFile ?? string.Empty;
            rpcFunctionMetadata.EntryPoint = functionMetadata.EntryPoint ?? string.Empty;
            return rpcFunctionMetadata;
        }

        public static InvocationRequest ToRpcInvocationRequest(this Dictionary<string, object> scriptExecutionContext)
        {
            InvocationRequest invocationRequest = new InvocationRequest()
            {
                FunctionId = (string)scriptExecutionContext["functionId"],
                InvocationId = (string)scriptExecutionContext["invocationId"],
            };

            // TraceWriter systemTraceWriter = (TraceWriter)scriptExecutionContext["systemTraceWriter"];

            // TODO replace console.writeLines
            Console.WriteLine("*** Invoke Function");
            if (scriptExecutionContext != null)
            {
                if ((Dictionary<string, object>)scriptExecutionContext["bindingData"] != null)
                {
                    invocationRequest.TriggerMetadata.Add(Utilities.GetMetadataFromDictionary((Dictionary<string, object>)scriptExecutionContext["bindingData"]));
                }

                if ((Dictionary<string, KeyValuePair<object, DataType>>)scriptExecutionContext["inputBindings"] != null)
                {
                    var inputBindings = (Dictionary<string, KeyValuePair<object, DataType>>)scriptExecutionContext["inputBindings"];
                    foreach (var inputBinding in inputBindings)
                    {
                        var item = inputBindings[inputBinding.Key];
                        TypedData typedData = new TypedData();
                        ParameterBinding parameterBinding = new ParameterBinding()
                        {
                            Name = inputBinding.Key
                        };

                        // TODO how to infer webhook Trigger
                        if (inputBinding.Key == "req" || inputBinding.Key == "request" || inputBinding.Key == "webhookReq")
                        {
                            RpcHttp httpRequest = Utilities.BuildRpcHttpMessage((Dictionary<string, object>)item.Key);
                            typedData.TypeVal = RpcDataType.Http;
                            typedData.HttpVal = httpRequest;
                            parameterBinding.Data = typedData;
                        }
                        else if (item.Key != null)
                        {
                            parameterBinding.Data = Utilities.ConvertObjectToTypedData(item.Key);

                            // TODO do we need to consider dataType set by the host?
                            // switch (dataType)
                            // {
                            //    case DataType.Binary:
                            //    case DataType.Stream:
                            //        typedData.TypeVal = RpcDataType.Bytes;
                            //        typedData.BytesVal = ByteString.CopyFrom((byte[])item.Key);
                            //        break;

                            // case DataType.String:
                            //    default:
                            //        typedData.TypeVal = RpcDataType.String;
                            //        typedData.StringVal = ConvertObjectToString(item.Key);
                            //        break;
                            // }
                        }
                        invocationRequest.InputData.Add(parameterBinding);
                    }

                    // TODO infer tiggerType
                    // funcMetadata.TriggerType = (string)scriptExecutionContext["_triggerType"];
                }

                // TODO close the stream only when done with the worker
                // await outgoingResponseStreamFromService.CompleteAsync();
            }
            return invocationRequest;
        }
    }
}
