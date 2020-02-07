// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RpcDataType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.TypedData.DataOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
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
                    return RpcMessageExtensionUtilities.ConvertFromHttpMessageToExpando(typedData.Http);
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

        public static async Task<TypedData> ToRpc(this object value, ILogger logger, Capabilities capabilities)
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
                typedData.Json = jobj.ToString(Formatting.None);
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
                typedData = await request.ToRpcHttp(logger, capabilities);
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

        internal static async Task<TypedData> ToRpcHttp(this HttpRequest request, ILogger logger, Capabilities capabilities)
        {
            var http = new RpcHttp()
            {
                Url = $"{(request.IsHttps ? "https" : "http")}://{request.Host}{request.Path}{request.QueryString}",
                Method = request.Method.ToString(),
                RawBody = null
            };
            var typedData = new TypedData
            {
                Http = http
            };

            foreach (var pair in request.Query)
            {
                var value = pair.Value.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    http.Query.Add(pair.Key, value);
                }
            }

            foreach (var pair in request.Headers)
            {
                if (ShouldIgnoreEmptyHeaderValues(capabilities) && string.IsNullOrEmpty(pair.Value.ToString()))
                {
                    continue;
                }

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
                logger.LogTrace("HttpContext has ClaimsPrincipal; parsing to gRPC.");
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
                    await PopulateBody(request, http, capabilities, logger);
                }
                else
                {
                    await PopulateBodyAndRawBody(request, http, capabilities, logger);
                }
            }

            return typedData;
        }

        private static async Task PopulateBody(HttpRequest request, RpcHttp http, Capabilities capabilities, ILogger logger)
        {
            object body = null;
            if (request.IsMediaTypeOctetOrMultipart() || IsRawBodyBytesRequested(capabilities))
            {
                body = await request.GetRequestBodyAsBytesAsync();
            }
            else
            {
                body = await request.ReadAsStringAsync();
            }
            http.Body = await body.ToRpc(logger, capabilities);
        }

        private static async Task PopulateBodyAndRawBody(HttpRequest request, RpcHttp http, Capabilities capabilities, ILogger logger)
        {
            object body = null;
            string rawBodyString = null;

            if (MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue mediaType))
            {
                if (string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    rawBodyString = await request.ReadAsStringAsync();
                    try
                    {
                        // REVIEW: We are json deserializing this to a JObject only to serialze
                        // it back to string below. Why?
                        body = JsonConvert.DeserializeObject(rawBodyString);
                    }
                    catch (JsonException)
                    {
                        body = rawBodyString;
                    }
                }
                else if (request.IsMediaTypeOctetOrMultipart())
                {
                    byte[] bytes = await request.GetRequestBodyAsBytesAsync();
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
                body = rawBodyString = await request.ReadAsStringAsync();
            }

            http.Body = await body.ToRpc(logger, capabilities);
            if (IsRawBodyBytesRequested(capabilities))
            {
                byte[] bytes = await request.GetRequestBodyAsBytesAsync();
                http.RawBody = await bytes.ToRpc(logger, capabilities);
            }
            else
            {
                http.RawBody = await rawBodyString.ToRpc(logger, capabilities);
            }
        }

        internal static TypedData ToRpcDefault(this object value)
        {
            // attempt POCO / array of pocos
            TypedData typedData = new TypedData();
            try
            {
                typedData.Json = JsonConvert.SerializeObject(value, Formatting.None);
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
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(RpcWorkerConstants.RawHttpBodyBytes));
        }

        private static bool IsBodyOnlySupported(Capabilities capabilities)
        {
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(RpcWorkerConstants.RpcHttpBodyOnly));
        }

        private static bool IsTypedDataCollectionSupported(Capabilities capabilities)
        {
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(RpcWorkerConstants.TypedDataCollection));
        }

        private static bool ShouldIgnoreEmptyHeaderValues(Capabilities capabilities)
        {
            return !string.IsNullOrEmpty(capabilities.GetCapabilityState(RpcWorkerConstants.IgnoreEmptyValuedRpcHttpHeaders));
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