﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.TypedData.DataOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal static class RpcMessageConversionExtensions
    {
        private static readonly JsonSerializerSettings _datetimeSerializerSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

        public static object ToObject(this TypedData typedData)
        {
            switch (typedData.DataCase)
            {
                case RpcDataType.Bytes:
                case RpcDataType.Stream:
                    return typedData.Bytes.ToByteArray();
                case RpcDataType.String:
                    return typedData.String;
                case RpcDataType.Json:
                    return JsonConvert.DeserializeObject(typedData.Json, _datetimeSerializerSettings);
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

        public static TypedData ToRpc(this object value)
        {
            TypedData typedData = new TypedData();

            if (value == null)
            {
                return typedData;
            }

            if (value is byte[] arr)
            {
                typedData.Bytes = ByteString.CopyFrom(arr);
            }
            else if (value is JObject jobj)
            {
                typedData.Json = jobj.ToString();
            }
            else if (value is string str)
            {
                typedData.String = str;
            }
            else if (value is HttpRequest request)
            {
                var http = new RpcHttp()
                {
                    Url = $"{(request.IsHttps ? "https" : "http")}://{request.Host.ToString()}{request.Path.ToString()}{request.QueryString.ToString()}", // [http|https]://{url}{path}{query}
                    Method = request.Method.ToString()
                };
                typedData.Http = http;

                http.RawBody = null;
                foreach (var pair in request.Query)
                {
                    if (!string.IsNullOrEmpty(pair.Value.ToString()))
                    {
                        http.Query.Add(pair.Key, pair.Value.ToString());
                    }
                }

                foreach (var pair in request.Headers)
                {
                    http.Headers.Add(pair.Key.ToLowerInvariant(), pair.Value.ToString());
                }

                if (request.HttpContext.Items.TryGetValue(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, out object routeData))
                {
                    Dictionary<string, object> parameters = (Dictionary<string, object>)routeData;
                    foreach (var pair in parameters)
                    {
                        if (pair.Value != null)
                        {
                            http.Params.Add(pair.Key, pair.Value.ToString());
                        }
                    }
                }

                // parse request body as content-type
                if (request.Body != null && request.ContentLength > 0)
                {
                    object body = null;
                    string rawBody = null;

                    MediaTypeHeaderValue mediaType = null;
                    if (MediaTypeHeaderValue.TryParse(request.ContentType, out mediaType))
                    {
                        if (string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                        {
                            var jsonReader = new StreamReader(request.Body, Encoding.UTF8);
                            rawBody = jsonReader.ReadToEnd();
                            try
                            {
                                body = JsonConvert.DeserializeObject(rawBody);
                            }
                            catch (JsonException)
                            {
                                body = rawBody;
                            }
                        }
                        else if (string.Equals(mediaType.MediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                            mediaType.MediaType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var length = Convert.ToInt32(request.ContentLength);
                            var bytes = new byte[length];
                            request.Body.Read(bytes, 0, length);
                            body = bytes;
                            rawBody = Encoding.UTF8.GetString(bytes);
                        }
                    }
                    // default if content-tye not found or recognized
                    if (body == null && rawBody == null)
                    {
                        var reader = new StreamReader(request.Body, Encoding.UTF8);
                        body = rawBody = reader.ReadToEnd();
                    }

                    request.Body.Position = 0;
                    http.Body = body.ToRpc();
                    http.RawBody = rawBody.ToRpc();
                }
            }
            else
            {
                // attempt POCO / array of pocos
                try
                {
                    typedData.Json = JsonConvert.SerializeObject(value);
                }
                catch
                {
                    typedData.String = value.ToString();
                }
            }
            return typedData;
        }
    }
}