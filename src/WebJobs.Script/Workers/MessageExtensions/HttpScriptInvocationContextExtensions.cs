// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Formatting;
using Microsoft.Azure.WebJobs.Script.Workers.Http;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public static class HttpScriptInvocationContextExtensions
    {
        public static HttpRequestMessage ToHttpRequestMessage(this HttpScriptInvocationContext context, string requestUri)
        {
            HttpRequestMessage httpRequest = new HttpRequestMessage
            {
                RequestUri = new Uri(requestUri),
                Method = HttpMethod.Post,
                Content = new ObjectContent<HttpScriptInvocationContext>(context, new JsonMediaTypeFormatter())
            };

            return httpRequest;
        }
    }
}
