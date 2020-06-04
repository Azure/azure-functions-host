// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.Middleware
{
    internal class DefaultMiddlewarePipeline : IJobHostMiddlewarePipeline
    {
        private static Lazy<IJobHostMiddlewarePipeline> _emptyPipeline = new Lazy<IJobHostMiddlewarePipeline>(() => new DefaultMiddlewarePipeline(Array.Empty<IJobHostHttpMiddleware>()));

        public DefaultMiddlewarePipeline(IEnumerable<IJobHostHttpMiddleware> middleware)
        {
            Pipeline = BuildPipeline(middleware);
        }

        public static IJobHostMiddlewarePipeline Empty => _emptyPipeline.Value;

        public RequestDelegate Pipeline { get; }

        private RequestDelegate BuildPipeline(IEnumerable<IJobHostHttpMiddleware> middleware)
        {
            RequestDelegate pipeline = context =>
            {
                if (context.Items.Remove(ScriptConstants.JobHostMiddlewarePipelineRequestDelegate, out object requestDelegate) && requestDelegate is RequestDelegate next)
                {
                    return next(context);
                }
                else
                {
                    return Task.CompletedTask;
                }
            };

            pipeline = middleware
                .Reverse()
                .Select(GetMiddlewareDelegate)
                .Aggregate(pipeline, (p, d) => d(p));

            return pipeline;
        }

        private static Func<RequestDelegate, RequestDelegate> GetMiddlewareDelegate(IJobHostHttpMiddleware middleware)
        {
            return next => context => middleware.Invoke(context, next);
        }
    }
}
