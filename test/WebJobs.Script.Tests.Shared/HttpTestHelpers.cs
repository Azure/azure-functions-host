// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Newtonsoft.Json;

namespace Microsoft.WebJobs.Script.Tests
{
    public class HttpTestHelpers
    {
        public static HttpRequest CreateHttpRequest(string method, string uriString, IHeaderDictionary headers = null, object body = null)
        {
            var uri = new Uri(uriString);
            var request = new DefaultHttpContext().Request;
            var requestFeature = request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = method;
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            headers = headers ?? new HeaderDictionary();

            if (!string.IsNullOrEmpty(uri.Host))
            {
                headers.Add("Host", uri.Host);
            }

            if (body != null)
            {
                byte[] bytes = null;
                if (body is string bodyString)
                {
                    bytes = Encoding.UTF8.GetBytes(bodyString);
                }
                else if (body is byte[] bodyBytes)
                {
                    bytes = bodyBytes;
                }
                else
                {
                    string bodyJson = JsonConvert.SerializeObject(body);
                    bytes = Encoding.UTF8.GetBytes(bodyJson);
                }

                requestFeature.Body = new MemoryStream(bytes);
                request.ContentLength = request.Body.Length;
                headers.Add("Content-Length", request.Body.Length.ToString());
            }

            requestFeature.Headers = headers;

            return request;
        }
    }
}
