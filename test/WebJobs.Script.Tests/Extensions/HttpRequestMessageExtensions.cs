// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class HttpRequestMessageExtensions
    {
        [Fact]
        public void GetRawHeaders_ReturnsExpectedHeaders()
        {
            // No headers
            HttpRequestMessage request = new HttpRequestMessage();
            var headers = request.GetRawHeaders();
            Assert.Equal(0, headers.Count);

            // One header
            request = new HttpRequestMessage();
            string testHeader1 = "TestValue";
            request.Headers.Add("Header1", testHeader1);
            headers = request.GetRawHeaders();
            Assert.Equal(1, headers.Count);
            Assert.Equal(testHeader1, headers["Header1"]);

            // Multiple headers
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.71 Safari/537.36";
            string testHeader2 = "foo,bar,baz";
            string testHeader3 = "foo bar baz";
            request.Headers.Add("User-Agent", userAgent);
            request.Headers.Add("Header2", testHeader2);
            request.Headers.Add("Header3", testHeader3);
            headers = request.GetRawHeaders();
            Assert.Equal(4, headers.Count);
            Assert.Equal(userAgent, headers["User-Agent"]);
            Assert.Equal(testHeader1, headers["Header1"]);
            Assert.Equal(testHeader2, headers["Header2"]);
            Assert.Equal(testHeader3, headers["Header3"]);
        }
    }
}
