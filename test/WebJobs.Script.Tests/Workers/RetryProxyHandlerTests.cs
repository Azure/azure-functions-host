// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class RetryProxyHandlerTests
    {
        [Fact]
        public async Task SendAsync_RetriesToMax()
        {
            var inner = new TestHandler();
            var handler = new RetryProxyHandler(inner, NullLogger.Instance);
            var request = new HttpRequestMessage();

            var response = typeof(RetryProxyHandler)!
                .GetMethod("SendAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(handler, new object[] { request, CancellationToken.None })
                as Task<HttpResponseMessage>;

            var result = await response.ContinueWith(t => t);

            Assert.True(result.IsFaulted);
            Assert.True(result.Exception.InnerException is HttpRequestException);
            Assert.Equal(RetryProxyHandler.MaxRetries, inner.Attempts);
        }

        private class TestHandler : HttpMessageHandler
        {
            public int Attempts { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Attempts++;

                throw new HttpRequestException();
            }
        }
    }
}
