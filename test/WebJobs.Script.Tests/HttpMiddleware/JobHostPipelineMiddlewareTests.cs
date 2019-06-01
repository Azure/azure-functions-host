// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpMiddleware
{
    public class JobHostPipelineMiddlewareTests
    {
        [Fact]
        public async Task Invoke_SetsRequestDelegate()
        {
            bool delegateInvoked = false;
            RequestDelegate requestDelegate = c =>
            {
                delegateInvoked = true;
                return Task.CompletedTask;
            };

            var middleware = new JobHostPipelineMiddleware(requestDelegate);

            var context = new DefaultHttpContext();
            var pipeline = new DefaultMiddlewarePipeline(new List<IJobHostHttpMiddleware>());

            await middleware.Invoke(context, pipeline);

            Assert.True(delegateInvoked);
        }
    }
}
