﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpRequestMessageExtensions
    {
        [Fact]
        public void GetHeaderValueOrDefault_ReturnsExpectedResult()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://foobar");
            string value = request.GetHeaderValueOrDefault("TestHeader");
            Assert.Null(value);

            request.Headers.Add("TestHeader", "One");
            value = request.GetHeaderValueOrDefault("TestHeader");
            Assert.Equal("One", value);

            request.Headers.Add("TestHeader", "Two");
            value = request.GetHeaderValueOrDefault("TestHeader");
            Assert.Equal("One", value);
        }

        [Fact]
        public void IsAntaresInternalRequest_ReturnsExpectedResult()
        {
            // not running under Azure
            var request = new HttpRequestMessage(HttpMethod.Get, "http://foobar");
            Assert.False(request.IsAntaresInternalRequest());

            // running under Azure
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // with header
                request = new HttpRequestMessage(HttpMethod.Get, "http://foobar");
                request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "123");
                Assert.False(request.IsAntaresInternalRequest());

                request = new HttpRequestMessage(HttpMethod.Get, "http://foobar");
                Assert.True(request.IsAntaresInternalRequest());
            }
        }

        [Fact]
        public void GetQueryParameterDictionary_ReturnsExpectedParameters()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://test.com/test?a=1&b=2&b=3&c=4&c=5&d=6");
            var parameters = request.GetQueryParameterDictionary();
            Assert.Equal(4, parameters.Count);
            Assert.Equal("1", parameters["a"]);
            Assert.Equal("3", parameters["b"]);
            Assert.Equal("5", parameters["c"]);
            Assert.Equal("6", parameters["d"]);
        }

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
            string accept = "text/html, application/xhtml+xml, application/xml; q=0.9, */*; q=0.8";
            string testHeader2 = "foo,bar,baz";
            string testHeader3 = "foo bar baz";
            request.Headers.Add("User-Agent", userAgent);
            request.Headers.Add("Accept", accept);
            request.Headers.Add("Header2", testHeader2);
            request.Headers.Add("Header3", testHeader3);
            request.Headers.Add("Empty", string.Empty);
            var str = request.Headers.ToString();
            headers = request.GetRawHeaders();
            Assert.Equal(6, headers.Count);
            Assert.Equal(userAgent, headers["User-Agent"]);
            Assert.Equal(accept, headers["Accept"]);
            Assert.Equal(testHeader1, headers["Header1"]);
            Assert.Equal(testHeader2, headers["Header2"]);
            Assert.Equal(testHeader3, headers["Header3"]);
            Assert.Equal(string.Empty, headers["Empty"]);

            // Content headers
            request.Content = new StringContent("test");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            request.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse("form-data; name=\"fieldName\"; filename=\"filename.jpg\"");
            request.Content.Headers.ContentRange = ContentRangeHeaderValue.Parse("bytes 200-1000/67589");
            headers = request.GetRawHeaders();
            Assert.Equal(9, headers.Count);
            Assert.Equal("text/html", headers["Content-Type"]);
            Assert.Equal("form-data; name=\"fieldName\"; filename=\"filename.jpg\"", headers["Content-Disposition"]);
            Assert.Equal("bytes 200-1000/67589", headers["Content-Range"]);
        }
    }
}
