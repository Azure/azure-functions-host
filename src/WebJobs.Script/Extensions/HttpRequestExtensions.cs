// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class HttpRequestExtensions
    {
        private static readonly PathString _adminRoot = new PathString("/admin");
        private static readonly PathString _adminDownloadRequestRoot = new PathString("/admin/functions/download");

        public static bool IsAdminRequest(this HttpRequest request)
        {
            return request.Path.StartsWithSegments(_adminRoot);
        }

        public static bool IsAdminDownloadRequest(this HttpRequest request)
        {
            return request.Path.StartsWithSegments(_adminDownloadRequestRoot);
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

        public static bool HasHeader(this HttpRequest request, string headerName)
        {
            return !string.IsNullOrEmpty(request.GetHeaderValueOrDefault(headerName));
        }

        public static bool HasHeaderValue(this HttpRequest request, string headerName, string value)
        {
            return string.Equals(request.GetHeaderValueOrDefault(headerName), value, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetHeaderValueOrDefault(this HttpRequest request, string headerName)
        {
            return request.Headers.TryGetValue(headerName, out var values) ? values[0] : null;
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
            environment = GetEnvironment(request, environment);
            if (!environment.IsAppService())
            {
                return false;
            }

            // this header will *always* be present on requests originating externally (i.e. going
            // through the Anatares front end). For requests originating internally it will NOT be
            // present.
            return !request.Headers.Keys.Contains(ScriptConstants.AntaresLogIdHeaderName);
        }

        public static bool IsPlatformInternalRequest(this HttpRequest request, IEnvironment environment = null)
        {
            environment = GetEnvironment(request, environment);
            if (!environment.IsAppService())
            {
                return false;
            }

            var header = request.Headers[ScriptConstants.AntaresPlatformInternal];
            string value = header.FirstOrDefault();
            return string.Compare(value, "true", StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static IEnvironment GetEnvironment(HttpRequest request, IEnvironment environment = null)
        {
            return environment ?? request.HttpContext.RequestServices?.GetService<IEnvironment>() ?? SystemEnvironment.Instance;
        }

        public static bool IsColdStart(this HttpRequest request)
        {
            return !string.IsNullOrEmpty(request.GetHeaderValueOrDefault(ScriptConstants.AntaresColdStartHeaderName));
        }

        public static Uri GetRequestUri(this HttpRequest request) => new Uri(request.GetDisplayUrl());

        public static async Task<byte[]> GetRequestBodyAsBytesAsync(this HttpRequest request)
        {
            // allows the request to be read multiple times
            request.EnableBuffering();

            byte[] bytes;
            using (var ms = new MemoryStream())
            using (var reader = new StreamReader(ms))
            {
                await request.Body.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            if (request.Body.CanSeek)
            {
                request.Body.Seek(0, SeekOrigin.Begin);
            }

            return bytes;
        }

        public static bool IsMediaTypeOctetOrMultipart(this HttpRequest request)
        {
            if (MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue mediaType))
            {
                return Utility.IsMediaTypeOctetOrMultipart(mediaType);
            }
            return false;
        }

        public static HttpRequestMessage ToHttpRequestMessage(this HttpRequest request, string requestUri)
        {
            HttpRequestMessage httpRequest = new HttpRequestMessage
            {
                RequestUri = new Uri(QueryHelpers.AddQueryString(requestUri, request.GetQueryCollectionAsDictionary()))
            };

            foreach (var header in request.Headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
            }

            httpRequest.Method = new HttpMethod(request.Method);

            // Copy body
            if (request.ContentLength != null && request.ContentLength > 0)
            {
                httpRequest.Content = new StreamContent(request.Body);
                httpRequest.Content.Headers.Add("Content-Length", request.ContentLength.ToString());
                if (!string.IsNullOrEmpty(request.ContentType))
                {
                    httpRequest.Content.Headers.Add("Content-Type", request.ContentType);
                }
            }

            return httpRequest;
        }

        public static async Task<JObject> GetRequestAsJObject(this HttpRequest request)
        {
            var jObjectHttp = new JObject();
            jObjectHttp["Url"] = $"{(request.IsHttps ? "https" : "http")}://{request.Host.ToString()}{request.Path.ToString()}{request.QueryString.ToString()}";
            jObjectHttp["Method"] = request.Method.ToString();
            if (request.Query != null)
            {
                jObjectHttp["Query"] = request.GetQueryCollectionAsJObject();
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
                jObjectHttp["Identities"] = GetUserIdentitiesAsJArray(request.HttpContext.User.Identities);
            }

            // parse request body as content-type
            if (request.Body != null && request.ContentLength > 0)
            {
                if (request.IsMediaTypeOctetOrMultipart())
                {
                    jObjectHttp["Body"] = await request.GetRequestBodyAsBytesAsync();
                }
                else
                {
                    jObjectHttp["Body"] = await request.ReadAsStringAsync();
                }
            }

            return jObjectHttp;
        }

        internal static JObject GetQueryCollectionAsJObject(this HttpRequest request)
        {
            return JObject.FromObject(request.GetQueryCollectionAsDictionary());
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

        internal static JArray GetUserIdentitiesAsJArray(IEnumerable<ClaimsIdentity> claimsIdentities)
        {
            return JArray.FromObject(claimsIdentities, new JsonSerializer
            {
                // Claims property in Identities had circular reference to property 'Subject'
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }
    }
}
