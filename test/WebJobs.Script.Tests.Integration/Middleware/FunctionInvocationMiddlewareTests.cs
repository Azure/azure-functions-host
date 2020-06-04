// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Middleware
{
    public class FunctionInvocationMiddlewareTests
    {
        [Fact]
        public void RequiresAuthz_ReturnsExpectedResult()
        {
            var uri = new Uri("http://localhost/test");
            var request = new DefaultHttpContext().Request;
            var requestFeature = request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = "GET";
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            requestFeature.Headers = new HeaderDictionary
            {
                { "Host", uri.Host }
            };

            // Function level auth requires authz
            FunctionMetadata metadata = new FunctionMetadata();
            var function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test", null, metadata, null, null, null, null);
            var attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "get")
            {
                Route = "test"
            };
            function.SetupGet(p => p.HttpTriggerAttribute).Returns(() => attribute);
            bool result = FunctionInvocationMiddleware.RequiresAuthz(request, function.Object);
            Assert.True(result);

            // Proxies don't require authz
            metadata = new ProxyFunctionMetadata(null);
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "get")
            {
                Route = "test"
            };
            function.SetupGet(p => p.HttpTriggerAttribute).Returns(() => attribute);
            result = FunctionInvocationMiddleware.RequiresAuthz(request, function.Object);
            Assert.False(result);

            // Anonymous functions don't require authz
            metadata = new FunctionMetadata();
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute(AuthorizationLevel.Anonymous, "get")
            {
                Route = "test"
            };
            function.SetupGet(p => p.HttpTriggerAttribute).Returns(() => attribute);
            result = FunctionInvocationMiddleware.RequiresAuthz(request, function.Object);
            Assert.False(result);

            // Anonymous functions with EasyAuth header require authz
            requestFeature.Headers = new HeaderDictionary
            {
                { ScriptConstants.EasyAuthIdentityHeader, "abc123" }
            };
            result = FunctionInvocationMiddleware.RequiresAuthz(request, function.Object);
            Assert.True(result);

            // Anonymous functions with key header require authz
            requestFeature.Headers = new HeaderDictionary
            {
                { AuthenticationLevelHandler.FunctionsKeyHeaderName, "abc123" }
            };
            result = FunctionInvocationMiddleware.RequiresAuthz(request, function.Object);
            Assert.True(result);

            // Anonymous functions with key query param require authz
            uri = new Uri("http://localhost/test?code=abc123");
            request = new DefaultHttpContext().Request;
            requestFeature = request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = "GET";
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);
            requestFeature.Headers = new HeaderDictionary
            {
                { "Host", uri.Host }
            };
            result = FunctionInvocationMiddleware.RequiresAuthz(request, function.Object);
            Assert.True(result);
        }
    }
}
