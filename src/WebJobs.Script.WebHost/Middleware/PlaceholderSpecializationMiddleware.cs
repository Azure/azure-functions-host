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
        private static readonly PathString WorkerLinkPath = new("/admin/workers");
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IStandbyManager _standbyManager;
        private readonly IEnvironment _environment;
        private RequestDelegate _invoke;
        private double _specialized = 0;

        public PlaceholderSpecializationMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment,
            IStandbyManager standbyManager, IEnvironment environment)
        {
            _next = next;
            _invoke = InvokeSpecializationCheck;
            _webHostEnvironment = webHostEnvironment;
            _standbyManager = standbyManager;
            _environment = environment;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            await _invoke(httpContext);
        }

        private async Task InvokeSpecializationCheck(HttpContext httpContext)
        {
            if (!_webHostEnvironment.InStandbyMode && _environment.IsContainerReady())
            {
                // We don't want AsyncLocal context (like Activity.Current) to flow
                // here as it will contain request details. Suppressing this context
                // prevents the request context from being captured by the host.
                Task specializeTask;
                using (System.Threading.ExecutionContext.SuppressFlow())
                {
                    specializeTask = _standbyManager.SpecializeHostAsync();
                }

                // A compute host starts only after a worker links, and specialization waits for that start,
                // so holding a link request here would deadlock.
                if (!specializeTask.IsCompleted && IsWorkerLinkRequest(httpContext.Request))
                {
                    await _next(httpContext);
                    return;
                }

                await specializeTask;

                if (Interlocked.CompareExchange(ref _specialized, 1, 0) == 0)
                {
                    Interlocked.Exchange(ref _invoke, _next);
                }
            }

            await _next(httpContext);
        }

        // Matches PUT admin/workers/{workerPodName}; the route is defined in Functions.Host's WorkerLinkController.
        internal static bool IsWorkerLinkRequest(HttpRequest request)
            => HttpMethods.IsPut(request.Method) &&
                request.Path.StartsWithSegments(WorkerLinkPath, StringComparison.OrdinalIgnoreCase, out PathString workerPath) &&
                workerPath.Value is { Length: > 1 };
    }
}
