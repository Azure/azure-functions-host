// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class HttpRequestExtensions
    {
        public static bool IsAdminRequest(this HttpRequest request)
        {
            return request.Path.StartsWithSegments("/admin");
        }

        public static TValue GetRequestPropertyOrDefault<TValue>(this HttpRequest request, string key)
        {
            if (request.HttpContext != null &&
                request.HttpContext.Items.TryGetValue(key, out object value))
            {
                return (TValue)value;
            }
            return default(TValue);
        }

        public static string GetRequestId(this HttpRequest request)
        {
            return request.GetRequestPropertyOrDefault<string>(ScriptConstants.AzureFunctionsRequestIdKey);
        }

        public static string GetHeaderValueOrDefault(this HttpRequest request, string headerName)
        {
            StringValues values;
            if (request.Headers.TryGetValue(headerName, out values))
            {
                return values.First();
            }
            return null;
        }

        public static TValue GetItemOrDefault<TValue>(this HttpRequest request, string key)
        {
            object value = null;
            if (request.HttpContext.Items.TryGetValue(key, out value))
            {
                return (TValue)value;
            }
            return default(TValue);
        }

        public static bool IsAppServiceInternalRequest(this HttpRequest request, IEnvironment environment = null)
        {
            environment = environment ?? SystemEnvironment.Instance;
            if (!environment.IsAppService())
            {
                return false;
            }

            // this header will *always* be present on requests originating externally (i.e. going
            // through the Anatares front end). For requests originating internally it will NOT be
            // present.
            return !request.Headers.Keys.Contains(ScriptConstants.AntaresLogIdHeaderName);
        }

        public static bool IsColdStart(this HttpRequest request)
        {
            return !string.IsNullOrEmpty(request.GetHeaderValueOrDefault(ScriptConstants.AntaresColdStartHeaderName));
        }

        public static Uri GetRequestUri(this HttpRequest request) => new Uri(request.GetDisplayUrl());

        public static byte[] GetRequestBodyAsBytes(this HttpRequest request)
        {
            var length = Convert.ToInt32(request.ContentLength);
            var bytes = new byte[length];
            request.Body.Read(bytes, 0, length);
            request.Body.Position = 0;
            return bytes;
        }

        public static bool IsMediaTypeOctetOrMultipart(this HttpRequest request)
        {
            if (MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue mediaType))
            {
                return mediaType != null && (string.Equals(mediaType.MediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                                mediaType.MediaType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return false;
        }

        public static async Task<JObject> GetRequestAsJObject(this HttpRequest request)
        {
            var jObjectHttp = new JObject();
            jObjectHttp["Url"] = $"{(request.IsHttps ? "https" : "http")}://{request.Host.ToString()}{request.Path.ToString()}{request.QueryString.ToString()}"; // [http|https]://{url}{path}{query}
            jObjectHttp["Method"] = request.Method.ToString();
            if (request.Query != null)
            {
                jObjectHttp["Query"] = request.GetQueryCollectionAsString();
            }
            if (request.Headers != null)
            {
                jObjectHttp["Headers"] = JObject.FromObject(request.Headers);
            }
            if (request.HttpContext.Items.TryGetValue(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, out object routeData))
            {
                Dictionary<string, object> parameters = (Dictionary<string, object>)routeData;
                if (parameters != null)
                {
                    jObjectHttp["Params"] = JObject.FromObject(parameters);
                }
            }

            if (request.HttpContext?.User?.Identities != null)
            {
                jObjectHttp["Identities"] = JsonConvert.SerializeObject(request.HttpContext.User.Identities);
            }

            // parse request body as content-type
            if (request.Body != null && request.ContentLength > 0)
            {
                if (request.IsMediaTypeOctetOrMultipart())
                {
                    jObjectHttp["Body"] = request.GetRequestBodyAsBytes();
                }
                else
                {
                    jObjectHttp["Body"] = await request.ReadAsStringAsync();
                }
            }

            return jObjectHttp;
        }

        internal static string GetQueryCollectionAsString(this HttpRequest request)
        {
            return JsonConvert.SerializeObject(request.GetQueryCollectionAsDictionary());
        }

        internal static IDictionary<string, string> GetQueryCollectionAsDictionary(this HttpRequest request)
        {
            var queryParamsDictionary = new Dictionary<string, string>();
            foreach (var key in request.Query.Keys)
            {
                request.Query.TryGetValue(key, out StringValues value);
                queryParamsDictionary.Add(key, value.ToString());
            }
            return queryParamsDictionary;
        }
    }
}
