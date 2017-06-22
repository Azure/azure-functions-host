// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if WEBHOOKS
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.WebHooks
{
    public class WebHookReceiverManagerTests
    {
        private const string TestKey = "1388a6b0d05eca2237f10e4a4641260b0a08f3a6";
        private const string TestId = "testclient";

        [Fact]
        public void ApplyHeaderValuesToQuery_NoHeadersPresent_ReturnsExpectedValue()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/api/test");
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test", request.RequestUri.ToString());

            request = new HttpRequestMessage(HttpMethod.Post, $"http://test.com/api/test?code={TestKey}&clientid={TestId}");
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?code={TestKey}&clientid={TestId}", request.RequestUri.ToString());
        }

        [Fact]
        public void ApplyHeaderValuesToQuery_CodeInHeaders_ReturnsExpectedValue()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://test.com/api/test?clientid={TestId}");
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestKey);
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?clientid={TestId}&code={TestKey}", request.RequestUri.ToString());
        }

        [Fact]
        public void ApplyHeaderValuesToQuery_CodeAndClientIdInHeadesr_ReturnsExpectedValue()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/api/test");
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestKey);
            request.Headers.Add(WebHookReceiverManager.FunctionsClientIdHeaderName, TestId);
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?code={TestKey}", request.RequestUri.ToString());
        }

        [Fact]
        public void ApplyHeaderValuesToQuery_ValuesInHeadersAndQuery_ReturnsExpectedValue()
        {
            // query string value takes precedence
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/api/test?code=foo&clientid=bar");
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestKey);
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?code=foo&clientid=bar", request.RequestUri.ToString());

            // case insensitive query param lookups
            request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/api/test?CODE=foo&clientid=bar");
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestKey);
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?CODE=foo&clientid=bar", request.RequestUri.ToString());

            // code via query param, id via header
            request = new HttpRequestMessage(HttpMethod.Post, $"http://test.com/api/test?code={TestKey}");
            request.Headers.Add(WebHookReceiverManager.FunctionsClientIdHeaderName, TestId);
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?code={TestKey}", request.RequestUri.ToString());

            // id via query param, code via header
            request = new HttpRequestMessage(HttpMethod.Post, $"http://test.com/api/test?clientid={TestId}");
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, TestKey);
            WebHookReceiverManager.ApplyHeaderValuesToQuery(request);
            Assert.Equal($"http://test.com/api/test?clientid={TestId}&code={TestKey}", request.RequestUri.ToString());
        }
    }
}
#endif