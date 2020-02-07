// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// A middleware responsible for the execution of the Job Host scoped middleware pipeline.
    /// </summary>
    internal class JobHostPipelineMiddleware
    {
        private readonly RequestDelegate _next;

        public JobHostPipelineMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext, IJobHostMiddlewarePipeline middleware)
        {
            httpContext.Items.Add(ScriptConstants.JobHostMiddlewarePipelineRequestDelegate, _next);
            return middleware.Pipeline(httpContext);
        }
    }
}
