// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Rpc.Messages.TypedData.Types.Type;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class Utilities
    {
        public static bool IsTcpEndpointAvailable(string addressArgument, int portNumber, TraceWriter systemTraceWriter)
        {
            TcpClient tcpClient = null;
            bool endPointAvailable = false;
            try
            {
                // systemTraceWriter.Verbose($"Trying to connect to host:{addressArgument} on port:{portNumber}.");
                tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = tcpClient.SendTimeout = 2000;
                IPAddress address;
                if (IPAddress.TryParse(addressArgument, out address))
                {
                    systemTraceWriter.Verbose($"address {address} .");
                    var endPoint = new IPEndPoint(address, portNumber);
                    tcpClient.Connect(endPoint);
                }
                else
                {
                    tcpClient.Connect(addressArgument, portNumber);
                }

                systemTraceWriter.Verbose($"TCP connect succeeded. host:{addressArgument} on port:{portNumber}..");
                endPointAvailable = true;
            }
            catch (Exception e)
            {
                systemTraceWriter.Verbose(e.StackTrace);

                if (e is SocketException || e is TimeoutException)
                {
                    systemTraceWriter.Verbose($"Not listening on port {portNumber}.");
                }
            }
            finally
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
            }
            systemTraceWriter.Verbose($"endPointAvailable: {endPointAvailable}");
            return endPointAvailable;
        }

        public static MapField<string, TypedData> GetMetadataFromDictionary(Dictionary<string, object> scriptExecutionContextDictionary, string key = "")
        {
            MapField<string, TypedData> itemsDictionary = new MapField<string, TypedData>();
            foreach (var item in scriptExecutionContextDictionary)
            {
                itemsDictionary.Add(item.Key, ConvertObjectToTypedData(item.Value));
            }
            return itemsDictionary;
        }

        public static RpcHttp BuildRpcHttpMessage(Dictionary<string, object> inputHttpMessage)
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
                    requestMessage.Params.Add(GetMetadataFromDictionary((Dictionary<string, object>)item.Value));
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

        public static object ConvertTypedDataToObject(TypedData typedData)
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

                    // TODO key request/response
                    return ConvertFromHttpMessageToExpando(typedData.HttpVal);
                default:

                    // TODO better exception
                    throw new InvalidOperationException("Unknown RpcDataType");
            }
        }

        public static object ConvertFromHttpMessageToExpando(RpcHttp inputMessage)
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
                if (inputMessage.IsRaw)
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

        public static ByteString ConvertObjectToByteString(object scriptExecutionContextValue, out DataType dataType, DataType inputDataType)
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

        public static TypedData ConvertObjectToTypedData(object scriptExecutionContextValue, string key = "")
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
            else if (scriptExecutionContextValue.GetType() == typeof(string) || scriptExecutionContextValue.GetType() == typeof(int))
            {
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = scriptExecutionContextValue.ToString();
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
                // throw new InvalidOperationException("did not find item type: " + scriptExecutionContextValue.GetType().FullName);
                typedData.TypeVal = RpcDataType.String;
                typedData.StringVal = scriptExecutionContextValue.ToString();
            }
            return typedData;
        }

        public static void LogHandler(RpcLog logMessage)
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
    }
}
