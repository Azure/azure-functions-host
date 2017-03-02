// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Rpc.Messages.TypedData.Types.Type;
using RpcMessageType = Microsoft.Azure.WebJobs.Script.Rpc.Messages.StreamingMessage.Types.Type;

namespace WebJobs.Script.Rpc
{
    public class GoogleRpcServer : FunctionRpc.FunctionRpcBase
    {
        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            while (await requestStream.MoveNext(CancellationToken.None))
            {
                var incomingMessage = requestStream.Current;
                switch (incomingMessage.Type)
                {
                    case RpcMessageType.StartStream:
                        // TODO initialize Request/Response streams in GoogleRPC.cs
                        break;
                    case RpcMessageType.WorkerInitResponse:
                        break;
                    case RpcMessageType.WorkerHeartbeat:
                        break;
                    case RpcMessageType.WorkerStatusResponse:
                        break;
                    case RpcMessageType.FileChangeEventResponse:
                        break;
                    case RpcMessageType.FunctionLoadResponse:
                        break;
                    case RpcMessageType.InvocationResponse:
                        await InvocationResponseHandler(incomingMessage.Content.Unpack<InvocationResponse>());
                        break;
                    case RpcMessageType.Log:
                        LogHandler(incomingMessage.Content.Unpack<Log>());
                        break;
                    default:

                        // TODO bette exception
                        throw new System.Exception("Invalid RpcMessageType");
                }

                // TODO send invocationRequest
            }
        }

        public static Task<Dictionary<string, object>> InvocationResponseHandler(InvocationResponse invocationResponse)
        {
            Dictionary<string, object> itemsDictionary = new Dictionary<string, object>();
            if (invocationResponse.OutputData?.Count > 0)
            {
                foreach (ParameterBinding outputParameterBinding in invocationResponse.OutputData)
                {
                    object objValue = ConvertTypedDataToObject(outputParameterBinding.Data);
                    itemsDictionary.Add(outputParameterBinding.Name, objValue);
                }
            }
            return Task.FromResult(itemsDictionary);
        }

        public async void InvocationRequestHandler(IServerStreamWriter<StreamingMessage> outgoingResponseStreamFromService, string functionId, string requestId, FunctionInvocationContext context, Dictionary<string, object> scriptExecutionContext)
        {
            InvocationRequest invocationRequest = new InvocationRequest()
            {
                FunctionId = functionId,
                InvocationId = context.ExecutionContext.InvocationId.ToString(),
                RequestId = requestId
            };

            // TraceWriter systemTraceWriter = (TraceWriter)scriptExecutionContext["systemTraceWriter"];

            // TODO replace console.writeLines
            Console.WriteLine("*** Invoke Function");
            if (scriptExecutionContext != null)
            {
                if ((Dictionary<string, object>)scriptExecutionContext["bindingData"] != null)
                {
                    invocationRequest.TriggerMetadata.Add(GetMetadataFromDictionary((Dictionary<string, object>)scriptExecutionContext["bindingData"]));
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
                            RpcHttp httpRequest = BuildRpcHttpMessage((Dictionary<string, object>)item.Key);
                            typedData.TypeVal = RpcDataType.Http;
                            typedData.HttpVal = httpRequest;
                        }
                        else if (item.Key != null)
                        {
                            parameterBinding.Data = ConvertObjectToTypedData(item.Key);

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

                StreamingMessage streamingMessage = new StreamingMessage()
                {
                    Type = RpcMessageType.InvocationRequest,
                    Content = Any.Pack(invocationRequest)
                };
                await outgoingResponseStreamFromService.WriteAsync(streamingMessage);

                // TODO close the stream only when done with the worker
                // await outgoingResponseStreamFromService.CompleteAsync();
            }
        }

        public static void LogHandler(Log logMessage)
        {
            // TODO get rest of the properties from log message
            JObject logData = JObject.Parse(logMessage.Message);
            string message = (string)logData["msg"];
            if (message != null)
            {
                try
                {
                    // TODO Initialize SystemTraceWriter
                    // TraceLevel level = (TraceLevel)System.Enum.Parse(typeof(TraceLevel), logData["lvl"].ToString());
                    // systemTraceWriter.Trace(new TraceEvent(level, message));
                }
                catch (ObjectDisposedException)
                {
                    // if a function attempts to write to a disposed
                    // TraceWriter. Might happen if a function tries to
                    // log after calling done()
                }
            }
        }

        private static TypedData ConvertObjectToTypedData(object scriptExecutionContextValue)
        {
            TypedData typedData = new TypedData();
            if (scriptExecutionContextValue == null)
            {
                return null;
            }

            // TODO simplify looking up types
            if (scriptExecutionContextValue.GetType().FullName.Contains("Byte"))
            {
                typedData.TypeVal = RpcDataType.Bytes;
                typedData.BytesVal = ByteString.CopyFrom((byte[])scriptExecutionContextValue);
            }
            else if (scriptExecutionContextValue.GetType() == typeof(string) || scriptExecutionContextValue.GetType() == typeof(int))
            {
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = scriptExecutionContextValue.ToString();
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Generic.Dictionary"))
            {
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = JObject.FromObject(scriptExecutionContextValue).ToString();
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("ExpandoObject"))
            {
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = Newtonsoft.Json.JsonConvert.SerializeObject(scriptExecutionContextValue);
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
                throw new InvalidOperationException("did not find item type: " + scriptExecutionContextValue.GetType().FullName);
            }
            return typedData;
        }

        private static ByteString ConvertObjectToByteString(object scriptExecutionContextValue, out DataType dataType, DataType inputDataType)
        {
            dataType = DataType.String;
            if (scriptExecutionContextValue == null)
            {
                return null;
            }
            if (scriptExecutionContextValue.GetType() == typeof(string) || scriptExecutionContextValue.GetType() == typeof(int))
            {
                return ByteString.CopyFromUtf8(scriptExecutionContextValue.ToString());
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Generic.Dictionary"))
            {
                JObject jobject = JObject.FromObject(scriptExecutionContextValue);
                return ByteString.CopyFromUtf8(jobject.ToString());
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("ExpandoObject"))
            {
                string jsonOfTest = Newtonsoft.Json.JsonConvert.SerializeObject(scriptExecutionContextValue);
                return ByteString.CopyFromUtf8(jsonOfTest);
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Byte") || inputDataType == DataType.Binary)
            {
                dataType = DataType.Binary;
                return ByteString.CopyFrom((byte[])scriptExecutionContextValue);
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Newtonsoft.Json.Linq.JObject"))
            {
                return ByteString.CopyFromUtf8(scriptExecutionContextValue.ToString());
            }
            else if (scriptExecutionContextValue.GetType().FullName.Contains("Newtonsoft.Json.Linq.JArray"))
            {
                return ByteString.CopyFromUtf8(scriptExecutionContextValue.ToString());
            }
            else
            {
                throw new InvalidOperationException("did not find item type: " + scriptExecutionContextValue.GetType().FullName);
            }
        }

        private static object ConvertFromHttpMessageToExpando(RpcHttp inputMessage, string key = "")
        {
            if (inputMessage == null)
            {
                return null;
            }
            if (inputMessage.RawResponse != null)
            {
                object rawResponseData = ConvertTypedDataToObject(inputMessage.RawResponse);
                try
                {
                    dynamic obj = JsonConvert.DeserializeObject<ExpandoObject>(rawResponseData.ToString());
                    return obj;
                }
                catch (System.Exception)
                {
                    return rawResponseData;
                }
            }
            dynamic expando = new ExpandoObject();
            expando.method = inputMessage.Method;
            expando.query = inputMessage.Query as IDictionary<string, string>;
            expando.statusCode = inputMessage.StatusCode;
            IDictionary<string, string> inputMessageHeaders = inputMessage.Headers as IDictionary<string, string>;
            IDictionary<string, object> headers = new Dictionary<string, object>();
            foreach (var item in inputMessageHeaders)
            {
                headers.Add(item.Key, item.Value);
            }
            expando.headers = headers;
            if (inputMessage.Body != null)
            {
                if (inputMessage.IsRaw && inputMessage.Body.TypeVal != RpcDataType.Bytes)
                {
                    expando.body = inputMessage.Body.StringVal;
                }
                else
                {
                    object bodyConverted = ConvertTypedDataToObject(inputMessage.Body);
                    try
                    {
                        dynamic d = JsonConvert.DeserializeObject<ExpandoObject>(bodyConverted.ToString());
                        expando.body = d;
                    }
                    catch (System.Exception)
                    {
                        expando.body = bodyConverted;
                    }
                }
                if (key == "res" && inputMessage.IsRaw)
                {
                    expando.isRaw = true;
                }
            }
            else
            {
                expando.body = null;
            }
            return expando;
        }

        private static object ConvertTypedDataToObject(TypedData typedData)
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
                default:

                    // TODO better exception
                    throw new InvalidOperationException("Unknown RpcDataType");
            }
        }

        private static MapField<string, TypedData> GetMetadataFromDictionary(Dictionary<string, object> scriptExecutionContextDictionary)
        {
            MapField<string, TypedData> itemsDictionary = new MapField<string, TypedData>();
            foreach (var item in scriptExecutionContextDictionary)
            {
                itemsDictionary.Add(item.Key, ConvertObjectToTypedData(item.Value));
            }
            return itemsDictionary;
        }

        private static RpcHttp BuildRpcHttpMessage(Dictionary<string, object> inputHttpMessage)
        {
            RpcHttp requestMessage = new RpcHttp();
            TypedData messageBody = new TypedData();
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
                    // TODO
                    // requestMessage.Params.Add(GetMetadataFromDictionary((Dictionary<string, object>)item.Value));
                }
                else
                {
                    throw new InvalidOperationException("Did not find req key");
                }
            }

            // TODO if raw header is data always bytes?
            // string rawHeader = null;
            // if (requestMessage.Headers.TryGetValue("raw", out rawHeader))
            // {
            //    if (bool.Parse(rawHeader))
            //    {
            //        inputDatatype = DataType.Binary;
            //    }
            // }

            if (bodyValue != null)
            {
                messageBody = ConvertObjectToTypedData(bodyValue);
                requestMessage.Body = messageBody;
            }
            return requestMessage;
        }
    }
}
