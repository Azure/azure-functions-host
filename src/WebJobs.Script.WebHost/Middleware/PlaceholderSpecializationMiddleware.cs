// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class PlaceholderSpecializationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IScriptHostManager _hostManager;
        private readonly Lazy<Task> _specializationTask;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private Func<HttpContext, Task> _invoke;

        public PlaceholderSpecializationMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment, IScriptHostManager hostManager)
        {
            _next = next;
            _hostManager = hostManager;
            _invoke = InvokeSpecializationCheck;
            _webHostEnvironment = webHostEnvironment;

            _specializationTask = new Lazy<Task>(SpecializeHost, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public async Task Invoke(HttpContext httpContext)
        {
            await _invoke(httpContext);
        }

        private async Task InvokeSpecializationCheck(HttpContext httpContext)
        {
            if (!_webHostEnvironment.InStandbyMode)
            {
                await _specializationTask.Value;
            }

            await _next(httpContext);
        }

        private Task InvokeNext(HttpContext httpContext)
        {
            return _next(httpContext);
        }

        private async Task SpecializeHost()
        {
            await _hostManager.RestartHostAsync();
            await _hostManager.DelayUntilHostReady();

            Interlocked.Exchange(ref _invoke, InvokeNext);
        }
    }
}
