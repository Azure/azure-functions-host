// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
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

        public static TypedData ToRpc(this object value, ILogger logger, Capabilities capabilities)
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
            else if (value.GetType().IsArray && IsTypedDataCollectionSupported(capabilities))
            {
                typedData = value.ToRpcCollection();
            }
            else if (value is HttpRequest request)
            {
                typedData = request.ToRpcHttp(logger, capabilities);
            }
            else
            {
                typedData = value.ToRpcDefault();
            }

            return typedData;
        }

        internal static TypedData ToRpcCollection(this object value)
        {
            TypedData typedData;
            if (value is byte[][] arrBytes)
            {
                typedData = arrBytes.ToRpcByteArray();
            }
            else if (value is string[] arrStr)
            {
                typedData = arrStr.ToRpcStringArray();
            }
            else if (value is double[] arrDouble)
            {
                typedData = arrDouble.ToRpcDoubleArray();
            }
            else if (value is long[] arrLong)
            {
                typedData = arrLong.ToRpcLongArray();
            }
            else
            {
                typedData = value.ToRpcDefault();
            }

            return typedData;
        }

        internal static TypedData ToRpcHttp(this HttpRequest request, ILogger logger, Capabilities capabilities)
        {
            TypedData typedData = new TypedData();
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

            // parse ClaimsPrincipal if exists
            if (request.HttpContext?.User?.Identities != null)
            {
                logger.LogDebug("HttpContext has ClaimsPrincipal; parsing to gRPC.");
                foreach (var id in request.HttpContext.User.Identities)
                {
                    var rpcClaimsIdentity = new RpcClaimsIdentity();
                    if (id.AuthenticationType != null)
                    {
                        rpcClaimsIdentity.AuthenticationType = new NullableString { Value = id.AuthenticationType };
                    }

                    if (id.NameClaimType != null)
                    {
                        rpcClaimsIdentity.NameClaimType = new NullableString { Value = id.NameClaimType };
                    }

                    if (id.RoleClaimType != null)
                    {
                        rpcClaimsIdentity.RoleClaimType = new NullableString { Value = id.RoleClaimType };
                    }

                    foreach (var claim in id.Claims)
                    {
                        if (claim.Type != null && claim.Value != null)
                        {
                            rpcClaimsIdentity.Claims.Add(new RpcClaim { Value = claim.Value, Type = claim.Type });
                        }
                    }

                    http.Identities.Add(rpcClaimsIdentity);
                }
            }

            // parse request body as content-type
            if (request.Body != null && request.ContentLength > 0)
            {
                if (IsBodyOnlySupported(capabilities))
                {
                    PopulateBody(request, http, capabilities, logger);
                }
                else
                {
                    PopulateBodyAndRawBody(request, http, capabilities, logger);
                }
            }

            return typedData;
        }

        private static void PopulateBody(HttpRequest request, RpcHttp http, Capabilities capabilities, ILogger logger)
        {
            object body = null;
            if ((MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue mediaType) && IsMediaTypeOctetOrMultipart(mediaType)) || IsRawBodyBytesRequested(capabilities))
            {
                body = RequestBodyToBytes(request);
            }
            else
            {
                body = GetStringRepresentationOfBody(request);
            }
            http.Body = body.ToRpc(logger, capabilities);
        }

        private static string GetStringRepresentationOfBody(HttpRequest request)
        {
            string result;
            using (StreamReader reader = new StreamReader(request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            {
                result = reader.ReadToEnd();
            }
            request.Body.Position = 0;
            return result;
        }

        private static void PopulateBodyAndRawBody(HttpRequest request, RpcHttp http, Capabilities capabilities, ILogger logger)
        {
            object body = null;
            string rawBodyString = null;
            byte[] bytes = RequestBodyToBytes(request);

            if (MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue mediaType))
            {
                if (string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    rawBodyString = GetStringRepresentationOfBody(request);
                    try
                    {
                        body = JsonConvert.DeserializeObject(rawBodyString);
                    }
                    catch (JsonException)
                    {
                        body = rawBodyString;
                    }
                }
                else if (IsMediaTypeOctetOrMultipart(mediaType))
                {
                    body = bytes;
                    if (!IsRawBodyBytesRequested(capabilities))
                    {
                        rawBodyString = Encoding.UTF8.GetString(bytes);
                    }
                }
            }
            // default if content-tye not found or recognized
            if (body == null && rawBodyString == null)
            {
                body = rawBodyString = GetStringRepresentationOfBody(request);
            }

            http.Body = body.ToRpc(logger, capabilities);
            if (IsRawBodyBytesRequested(capabilities))
            {
                http.RawBody = bytes.ToRpc(logger, capabilities);
            }
            else
            {
                http.RawBody = rawBodyString.ToRpc(logger, capabilities);
            }
        }

        private static bool IsMediaTypeOctetOrMultipart(MediaTypeHeaderValue mediaType)
        {
            return mediaType != null && (string.Equals(mediaType.MediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                            mediaType.MediaType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static TypedData ToRpcDefault(this object value)
        {
            // attempt POCO / array of pocos
            TypedData typedData = new TypedData();
            try
            {
                typedData.Json = JsonConvert.SerializeObject(value);
            }
            catch
            {
                typedData.String = value.ToString();
            }

            return typedData;
        }

        internal static TypedData ToRpcByteArray(this byte[][] arrBytes)
        {
            TypedData typedData = new TypedData();
            CollectionBytes collectionBytes = new CollectionBytes();
            foreach (byte[] element in arrBytes)
            {
                if (element != null)
                {
                    collectionBytes.Bytes.Add(ByteString.CopyFrom(element));
                }
            }
            typedData.CollectionBytes = collectionBytes;

            return typedData;
        }

        internal static TypedData ToRpcStringArray(this string[] arrString)
        {
            TypedData typedData = new TypedData();
            CollectionString collectionString = new CollectionString();
            foreach (string element in arrString)
            {
                if (!string.IsNullOrEmpty(element))
                {
                    collectionString.String.Add(element);
                }
            }
            typedData.CollectionString = collectionString;

            return typedData;
        }

        internal static TypedData ToRpcDoubleArray(this double[] arrDouble)
        {
            TypedData typedData = new TypedData();
            CollectionDouble collectionDouble = new CollectionDouble();
            foreach (double element in arrDouble)
            {
                collectionDouble.Double.Add(element);
            }
            typedData.CollectionDouble = collectionDouble;

            return typedData;
        }

        internal static TypedData ToRpcLongArray(this long[] arrLong)
        {
            TypedData typedData = new TypedData();
            CollectionSInt64 collectionLong = new CollectionSInt64();
            foreach (long element in arrLong)
            {
                collectionLong.Sint64.Add(element);
            }
            typedData.CollectionSint64 = collectionLong;

            return typedData;
        }

        private static bool IsRawBodyBytesRequested(Capabilities capabilities)
        {
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(LanguageWorkerConstants.RawHttpBodyBytes));
        }

        private static bool IsBodyOnlySupported(Capabilities capabilities)
        {
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(LanguageWorkerConstants.RpcHttpBodyOnly));
        }

        private static bool IsTypedDataCollectionSupported(Capabilities capabilities)
        {
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(LanguageWorkerConstants.TypedDataCollection));
        }

        internal static byte[] RequestBodyToBytes(HttpRequest request)
        {
            var length = Convert.ToInt32(request.ContentLength);
            var bytes = new byte[length];
            request.Body.Read(bytes, 0, length);
            request.Body.Position = 0;
            return bytes;
        }

        public static BindingInfo ToBindingInfo(this BindingMetadata bindingMetadata)
        {
            BindingInfo bindingInfo = new BindingInfo
            {
                Direction = (BindingInfo.Types.Direction)bindingMetadata.Direction,
                Type = bindingMetadata.Type
            };

            if (bindingMetadata.DataType != null)
            {
                bindingInfo.DataType = (BindingInfo.Types.DataType)bindingMetadata.DataType;
            }

            return bindingInfo;
        }
    }
}