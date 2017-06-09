// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                            RpcHttp httpRequest = ToRpcHttpMessage((Dictionary<string, object>)item.Key);
                            typedData.TypeVal = RpcDataType.Http;
                            typedData.HttpVal = httpRequest;
                            parameterBinding.Data = typedData;
                        }
                        else if (item.Key != null)
                        {
                            parameterBinding.Data = item.Key.ToRpcTypedData();
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

        public static RpcHttp ToRpcHttpMessage(this Dictionary<string, object> inputHttpMessage)
        {
            RpcHttp requestMessage = new RpcHttp();
            object bodyValue = null;
            foreach (var item in inputHttpMessage)
            {
                if (item.Key == "headers")
                {
                    var headers = item.Value as IDictionary<string, string>;
                    requestMessage.Headers.Add(headers);
                }
                else if (item.Key == "query")
                {
                    var query = item.Value as IDictionary<string, string>;
                    requestMessage.Query.Add(query);
                }
                else if (item.Key == "method")
                {
                    requestMessage.Method = (string)item.Value;
                }
                else if (item.Key == "originalUrl")
                {
                    requestMessage.Url = (string)item.Value;
                }
                else if (item.Key == "body")
                {
                    bodyValue = item.Value;
                }
                else if (item.Key == "rawBody")
                {
                    requestMessage.RawBody = item.Value.ToString();
                }
                else if (item.Key == "params")
                {
                    requestMessage.Params.Add(Utilities.GetMetadataFromDictionary((Dictionary<string, object>)item.Value));
                }
                else
                {
                    throw new InvalidOperationException("Did not find req key");
                }
            }

            if (bodyValue != null)
            {
                requestMessage.Body = bodyValue.ToRpcTypedData();
            }
            return requestMessage;
        }

        public static object FromRpcTypedDataToObject(this TypedData typedData)
        {
            switch (typedData.TypeVal)
            {
                case RpcDataType.Bytes:
                case RpcDataType.Stream:
                    return typedData.BytesVal.ToByteArray();
                case RpcDataType.String:
                    return typedData.StringVal;
                case RpcDataType.Json:
                    return JObject.Parse(typedData.StringVal);
                case RpcDataType.Http:
                    return Utilities.ConvertFromHttpMessageToExpando(typedData.HttpVal);
                default:
                    // TODO better exception
                    throw new InvalidOperationException("Unknown RpcDataType");
            }
        }

        public static TypedData ToRpcTypedData(this object scriptExecutionContextValue)
        {
            TypedData typedData = new TypedData();
            if (scriptExecutionContextValue == null)
            {
                return typedData;
            }

            if (scriptExecutionContextValue.GetType().FullName.Contains("Byte"))
            {
                typedData.TypeVal = RpcDataType.Bytes;
                typedData.BytesVal = ByteString.CopyFrom((byte[])scriptExecutionContextValue);
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Generic.Dictionary"))
            {
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = JObject.FromObject(scriptExecutionContextValue).ToString();
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("ExpandoObject"))
            {
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = JsonConvert.SerializeObject(scriptExecutionContextValue);
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Newtonsoft.Json.Linq.JObject"))
            {
                typedData.TypeVal = RpcDataType.Json;
                typedData.StringVal = scriptExecutionContextValue.ToString();
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Newtonsoft.Json.Linq.JArray"))
            {
                typedData.TypeVal = RpcDataType.Json;
                typedData.StringVal = scriptExecutionContextValue.ToString();
            }
            else
            {
                // default to string

                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = scriptExecutionContextValue.ToString();
            }
            return typedData;
        }
    }
}
