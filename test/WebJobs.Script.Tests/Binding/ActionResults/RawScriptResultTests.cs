// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Binding.ActionResults
{
    public class RawScriptResultTests
    {
        [Fact]
        public void HasExpectedProperties_WithStatusCode()
        {
            var obj = "hello world";
            var result = new RawScriptResult(202, obj) { Headers = new Dictionary<string, object>() };

            Assert.Equal(202, result.StatusCode);
            Assert.Equal(obj, result.Content);
            Assert.Empty(result.Headers);
        }

        [Fact]
        public void HasExpectedProperties_WithoutStatusCode()
        {
            var obj = "hello world";
            var result = new RawScriptResult(null, obj) { Headers = new Dictionary<string, object>() };

            Assert.Null(result.StatusCode);
            Assert.Equal(obj, result.Content);
            Assert.Empty(result.Headers);
        }

        [Fact]
        public async Task HandlesStringContent()
        {
            var obj = "{ \"a\": 1 }";
            var result = new RawScriptResult(null, obj) { Headers = new Dictionary<string, object>() };
            var context = new ActionContext() { HttpContext = new DefaultHttpContext() };
            context.HttpContext.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(context);
            var body = await TestHelpers.ReadStreamToEnd(context.HttpContext.Response.Body);
            Assert.Equal(obj, body);
            Assert.Equal(200, context.HttpContext.Response.StatusCode);
        }

        [Fact]
        public async Task HandlesStringContent_WithHeader()
        {
            var obj = "{ \"a\": 1 }";
            var contentHeader = "application/json";
            var result = new RawScriptResult(null, obj) { Headers = new Dictionary<string, object>() };
            var context = new ActionContext() { HttpContext = new DefaultHttpContext() };
            context.HttpContext.Response.Headers.Add("Content-Type", contentHeader);
            context.HttpContext.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(context);
            var body = await TestHelpers.ReadStreamToEnd(context.HttpContext.Response.Body);
            StringValues value;
            Assert.True(context.HttpContext.Response.Headers.TryGetValue("Content-Type", out value));
            Assert.Equal("application/json; charset=utf-8", value);
            Assert.Equal(obj, body);
            Assert.Equal(200, context.HttpContext.Response.StatusCode);
        }

        [Fact]
        public async Task HandlesByteContent()
        {
            var content = "{ \"a\": 1 }";
            var obj = Encoding.UTF8.GetBytes(content);
            var result = new RawScriptResult(null, obj) { Headers = new Dictionary<string, object>() };
            var context = new ActionContext() { HttpContext = new DefaultHttpContext() };
            context.HttpContext.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(context);
            var body = await TestHelpers.ReadStreamToEnd(context.HttpContext.Response.Body);
            Assert.Equal(content, body);
            Assert.Equal(200, context.HttpContext.Response.StatusCode);
        }

        [Fact]
        public async Task HandlesStreamContent()
        {
            var content = "{ \"a\": 1 }";
            var obj = new MemoryStream(Encoding.UTF8.GetBytes(content));
            var result = new RawScriptResult(null, obj) { Headers = new Dictionary<string, object>() };
            var context = new ActionContext() { HttpContext = new DefaultHttpContext() };
            context.HttpContext.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(context);
            var body = await TestHelpers.ReadStreamToEnd(context.HttpContext.Response.Body);
            Assert.Equal(content, body);
            Assert.Equal(200, context.HttpContext.Response.StatusCode);
        }
    }
}
