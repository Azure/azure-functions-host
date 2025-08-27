// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Http;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Http
{
    public class ScriptInvocationRequestTransformerTests
    {
        private readonly ScriptInvocationRequestTransformer _transformer;

        public ScriptInvocationRequestTransformerTests()
        {
            _transformer = ScriptInvocationRequestTransformer.Instance;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TransformRequestAsync_IncludesXForwardedHeaders(bool includeScriptInvocationContext)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.com", 443);
            httpContext.Request.PathBase = "/api";
            httpContext.Request.Path = "/test";
            httpContext.Request.QueryString = new QueryString("?param=value");

            var remoteAddress = "192.168.1.100";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteAddress);

            if (includeScriptInvocationContext)
            {
                var scriptContext = new ScriptInvocationContext
                {
                    FunctionMetadata = new FunctionMetadata { Name = "TestFunction" },
                    ExecutionContext = new ExecutionContext { InvocationId = Guid.NewGuid() }
                };

                httpContext.Items[ScriptConstants.HttpProxyScriptInvocationContext] = scriptContext;
            }

            var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost:7071/api/test");
            const string destinationPrefix = "http://localhost:7071";

            await _transformer.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, CancellationToken.None);

            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-For"), "X-Forwarded-For header should be present");
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Host"), "X-Forwarded-Host header should be present");
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Proto"), "X-Forwarded-Proto header should be present");

            var forwardedFor = proxyRequest.Headers.GetValues("X-Forwarded-For");
            Assert.Contains(remoteAddress, forwardedFor);

            var forwardedHost = proxyRequest.Headers.GetValues("X-Forwarded-Host");
            Assert.Contains("example.com:443", forwardedHost);

            var forwardedProto = proxyRequest.Headers.GetValues("X-Forwarded-Proto");
            Assert.Contains("https", forwardedProto);
        }

        [Fact]
        public async Task TransformRequestAsync_WithScriptInvocationContext_AddsContextToRequestOptions()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost", 7071);
            var remoteAddress = "192.168.1.100";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteAddress);

            var scriptContext = new ScriptInvocationContext
            {
                FunctionMetadata = new FunctionMetadata { Name = "TestFunction" },
                ExecutionContext = new ExecutionContext { InvocationId = Guid.NewGuid() }
            };

            httpContext.Items[ScriptConstants.HttpProxyScriptInvocationContext] = scriptContext;

            var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost:7071/api/test");
            const string destinationPrefix = "http://localhost:7071";

            await _transformer.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, CancellationToken.None);

            Assert.True(proxyRequest.Options.TryGetValue(ScriptConstants.HttpProxyScriptInvocationContext, out ScriptInvocationContext contextValue));
            Assert.Equal(scriptContext.ExecutionContext.InvocationId, contextValue.ExecutionContext.InvocationId);

            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-For"));
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Host"));
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Proto"));
        }

        [Fact]
        public async Task TransformRequestAsync_PreservesExistingXForwardedHeaders()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("proxy.example.com");
            var requestRemoteAddress = "172.16.0.1";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(requestRemoteAddress);

            // Add existing X-Forwarded headers to simulate request through multiple proxies
            var originalFor = "203.0.113.195," + requestRemoteAddress;
            var originalHost = "proxy.example.com";
            var originalProto = "https";
            httpContext.Request.Headers["X-Forwarded-For"] = originalFor;
            httpContext.Request.Headers["X-Forwarded-Host"] = originalHost;
            httpContext.Request.Headers["X-Forwarded-Proto"] = originalProto;

            var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost:7071/api/test");
            const string destinationPrefix = "http://localhost:7071";

            await _transformer.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, CancellationToken.None);

            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-For"));
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Host"));
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Proto"));

            var forwardedFor = proxyRequest.Headers.GetValues("X-Forwarded-For");
            Assert.Contains(requestRemoteAddress, forwardedFor);

            var forwardedHost = proxyRequest.Headers.GetValues("X-Forwarded-Host");
            Assert.Contains(originalHost, forwardedHost);

            var forwardedProto = proxyRequest.Headers.GetValues("X-Forwarded-Proto");
            Assert.Contains(originalProto, forwardedProto);
        }

        [Fact]
        public async Task TransformRequestAsync_PreservesStandardRequestHeaders()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.com");
            httpContext.Request.Path = "/api/test";
            var remoteAddress = "192.168.1.100";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteAddress);

            // Add various standard headers that should be preserved
            httpContext.Request.Headers["Authorization"] = "Bearer token123";
            httpContext.Request.Headers["User-Agent"] = "TestClient/1.0";
            httpContext.Request.Headers["Accept"] = "application/json";
            httpContext.Request.Headers["Content-Type"] = "application/json";
            httpContext.Request.Headers["X-Custom-Header"] = "custom-value";
            httpContext.Request.Headers["Cache-Control"] = "no-cache";
            httpContext.Request.Headers["Accept-Encoding"] = "gzip, deflate";

            var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:7071/api/test");
            const string destinationPrefix = "http://localhost:7071";

            await _transformer.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, CancellationToken.None);

            // Verify that standard headers are preserved
            Assert.True(proxyRequest.Headers.Contains("Authorization"), "Authorization header should be preserved");
            Assert.True(proxyRequest.Headers.Contains("User-Agent"), "User-Agent header should be preserved");
            Assert.True(proxyRequest.Headers.Contains("Accept"), "Accept header should be preserved");
            Assert.True(proxyRequest.Headers.Contains("X-Custom-Header"), "Custom headers should be preserved");
            Assert.True(proxyRequest.Headers.Contains("Cache-Control"), "Cache-Control header should be preserved");
            Assert.True(proxyRequest.Headers.Contains("Accept-Encoding"), "Accept-Encoding header should be preserved");

            // Verify header values
            var authHeader = proxyRequest.Headers.GetValues("Authorization");
            Assert.Contains("Bearer token123", authHeader);

            var userAgentHeader = proxyRequest.Headers.GetValues("User-Agent");
            Assert.Contains("TestClient/1.0", userAgentHeader);

            var acceptHeader = proxyRequest.Headers.GetValues("Accept");
            Assert.Contains("application/json", acceptHeader);

            var customHeader = proxyRequest.Headers.GetValues("X-Custom-Header");
            Assert.Contains("custom-value", customHeader);

            var cacheControlHeader = proxyRequest.Headers.GetValues("Cache-Control");
            Assert.Contains("no-cache", cacheControlHeader);

            var acceptEncodingHeader = proxyRequest.Headers.GetValues("Accept-Encoding");
            Assert.Contains("gzip", acceptEncodingHeader);
            Assert.Contains("deflate", acceptEncodingHeader);

            // Verify that Content-Type is properly handled for the request content
            if (proxyRequest.Content != null)
            {
                Assert.Equal("application/json", proxyRequest.Content.Headers.ContentType?.MediaType);
            }

            // Also verify X-Forwarded headers are still added
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-For"));
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Host"));
            Assert.True(proxyRequest.Headers.Contains("X-Forwarded-Proto"));
        }
    }
}