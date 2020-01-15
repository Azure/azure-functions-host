// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class AppServiceHeaderFixupMiddlewareTests
    {
        [Theory]
        [InlineData(new[] { "http" }, "http")]
        [InlineData(new[] { "https" }, "https")]
        [InlineData(new[] { "https", "http" }, "https")]
        [InlineData(new[] { "http", "https" }, "http")]
        public async Task AppServiceFixupMiddleware_Handles_Multivalue_Header(string[] headerValues, string expectedScheme)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Add(AppServiceHeaderFixupMiddleware.ForwardedProtocolHeader, new StringValues(headerValues));

            var middleware = new AppServiceHeaderFixupMiddleware(nextCtx =>
            {
                nextCtx.Request.Scheme.Should().Be(expectedScheme);
                return Task.CompletedTask;
            });

            await middleware.Invoke(ctx);
        }
    }
}