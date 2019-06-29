// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpMiddleware
{
    public class DefaultMiddlewarePipelineTests
    {
        [Fact]
        public async Task RequestMiddleware_IsExecuted()
        {
            var pipeline = new DefaultMiddlewarePipeline(new List<IJobHostHttpMiddleware>());

            var context = new DefaultHttpContext();

            RequestDelegate requestDelegate = c =>
            {
                c.Items.Add(nameof(requestDelegate), string.Empty);

                return Task.CompletedTask;
            };

            context.Items.Add(ScriptConstants.JobHostMiddlewarePipelineRequestDelegate, requestDelegate);

            await pipeline.Pipeline(context);

            Assert.Contains(nameof(requestDelegate), context.Items);
        }

        [Fact]
        public async Task Middleware_IsExecutedInOrder()
        {
            var middleware = new List<IJobHostHttpMiddleware>
            {
                new TestMiddleware1(),
                new TestMiddleware2()
            };

            var pipeline = new DefaultMiddlewarePipeline(middleware);

            var context = new DefaultHttpContext();
            context.Items["middlewarelist"] = string.Empty;

            RequestDelegate requestDelegate = c =>
            {
                c.Items.Add(nameof(requestDelegate), string.Empty);

                return Task.CompletedTask;
            };

            context.Items.Add(ScriptConstants.JobHostMiddlewarePipelineRequestDelegate, requestDelegate);

            await pipeline.Pipeline(context);

            Assert.Contains(nameof(TestMiddleware1) + nameof(TestMiddleware2), context.Items["middlewarelist"].ToString());
        }

        private class TestMiddleware1 : IJobHostHttpMiddleware
        {
            public async Task Invoke(HttpContext context, RequestDelegate next)
            {
                context.Items["middlewarelist"] += nameof(TestMiddleware1);
                await next(context);
            }
        }

        private class TestMiddleware2 : IJobHostHttpMiddleware
        {
            public async Task Invoke(HttpContext context, RequestDelegate next)
            {
                context.Items["middlewarelist"] += nameof(TestMiddleware2);

                await next(context);
            }
        }
    }
}
