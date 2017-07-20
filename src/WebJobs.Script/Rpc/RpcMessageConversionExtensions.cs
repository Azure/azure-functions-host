// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Rpc.Messages.TypedData.DataOneofCase;

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
                            typedData.Http = httpRequest;
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
                else if (item.Key == "params")
                {
                    var parameters = item.Value as IDictionary<string, string>;
                    requestMessage.Params.Add(parameters);
                }
                else if (item.Key == "rawBody")
                {
                    // explicity ignored
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

        public static object FromRpcTypedDataToObject(this TypedData typedData, bool useExpando = true)
        {
            switch (typedData.DataCase)
            {
                case RpcDataType.Bytes:
                case RpcDataType.Stream:
                    return typedData.Bytes.ToByteArray();
                case RpcDataType.String:
                    return typedData.String;
                case RpcDataType.Json:
                    if (useExpando)
                    {
                        try
                        {
                            return JsonConvert.DeserializeObject<ExpandoObject>(typedData.Json, new ExpandoObjectConverter());
                        }
                        catch
                        {
                            // TODO: improve array handling - FunctionBinding.ReadAsEnumerable
                            var jarray = JArray.Parse(typedData.Json);
                            return jarray.AsJEnumerable().ToArray();
                        }
                    }
                    else
                    {
                        return JObject.Parse(typedData.Json);
                    }
                case RpcDataType.Http:
                    return Utilities.ConvertFromHttpMessageToExpando(typedData.Http);
                case RpcDataType.Int:
                    return typedData.Int;
                case RpcDataType.Double:
                    return typedData.Double;
                case RpcDataType.None:
                    return null;
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
                typedData.Bytes = ByteString.CopyFrom((byte[])scriptExecutionContextValue);
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Generic.Dictionary"))
            {
                typedData.String = JObject.FromObject(scriptExecutionContextValue).ToString();
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("ExpandoObject"))
            {
                typedData.String = JsonConvert.SerializeObject(scriptExecutionContextValue);
            }
            else
            {
                // default to string (JObject, JArray, etc)
                typedData.String = scriptExecutionContextValue.ToString();
            }
            return typedData;
        }
    }
}
