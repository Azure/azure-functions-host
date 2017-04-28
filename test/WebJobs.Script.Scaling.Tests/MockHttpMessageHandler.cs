// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _reasonPhrase;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string reasonPhrase = null)
        {
            _statusCode = statusCode;
            _reasonPhrase = reasonPhrase ?? statusCode.ToString();
        }

        public string ActivityId
        {
            get;
            private set;
        }

        public Uri Uri
        {
            get;
            private set;
        }

        public string Host
        {
            get;
            private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(0);

            var response = new HttpResponseMessage(_statusCode)
            {
                ReasonPhrase = _reasonPhrase
            };

            ActivityId = request.Headers.GetValues("x-ms-request-id").First();
            Uri = request.RequestUri;
            Host = request.Headers.Host;

            return response;
        }
    }
}