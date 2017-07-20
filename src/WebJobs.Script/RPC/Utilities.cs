// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

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

        public static MapField<string, TypedData> GetMetadataFromDictionary(Dictionary<string, object> scriptExecutionContextDictionary)
        {
            MapField<string, TypedData> itemsDictionary = new MapField<string, TypedData>();
            foreach (var item in scriptExecutionContextDictionary)
            {
                itemsDictionary.Add(item.Key, item.Value.ToRpcTypedData());
            }
            return itemsDictionary;
        }

        public static object ConvertFromHttpMessageToExpando(RpcHttp inputMessage)
        {
            if (inputMessage == null)
            {
                return null;
            }

            dynamic expando = new ExpandoObject();
            expando.method = inputMessage.Method;
            expando.query = inputMessage.Query as IDictionary<string, string>;
            expando.statusCode = inputMessage.StatusCode;
            expando.headers = inputMessage.Headers.ToDictionary(p => p.Key, p => (object)p.Value);
            expando.isRaw = inputMessage.IsRaw;

            if (inputMessage.Body != null)
            {
                expando.body = inputMessage.Body.FromRpcTypedDataToObject(true);
            }
            return expando;
        }
    }
}