// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public partial class HostAvailabilityCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HostAvailabilityCheckMiddleware> _logger;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IScriptHostManager _scriptHostManager;

        public HostAvailabilityCheckMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IScriptHostManager scriptHostManager)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger<HostAvailabilityCheckMiddleware>();
            _applicationHostOptions = applicationHostOptions;
            _scriptHostManager = scriptHostManager;
        }

        public Task Invoke(HttpContext httpContext)
        {
            // Slow path with async/await for spin-up only
            static async Task InvokeAwaited(HttpContext context, RequestDelegate next, ILogger<HostAvailabilityCheckMiddleware> logger, IScriptHostManager scriptHostManager)
            {
                using (Logger.VerifyingHostAvailabilityScope(logger, context.TraceIdentifier))
                {
                    Logger.InitiatingHostAvailabilityCheck(logger);

                    bool hostReady = await scriptHostManager.DelayUntilHostReady();
                    if (!hostReady)
                    {
                        Logger.HostUnavailableAfterCheck(logger);

                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("Function host is not running.");

                        return;
                    }

                    Logger.HostAvailabilityCheckSucceeded(logger);
                }

                await next.Invoke(context);
            }

            if (_scriptHostManager.State != ScriptHostState.Offline)
            {
                if (!_scriptHostManager.CanInvoke())
                {
                    return InvokeAwaited(httpContext, _next, _logger, _scriptHostManager);
                }

                return _next.Invoke(httpContext);
            }
            else
            {
                return httpContext.SetOfflineResponseAsync(_applicationHostOptions.CurrentValue.ScriptPath);
            }
        }
    }
}