// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public partial class HostAvailabilityCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HostAvailabilityCheckMiddleware> _logger;
        private readonly IOptions<ScriptApplicationHostOptions> _applicationHostOptions;

        public HostAvailabilityCheckMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IOptions<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger<HostAvailabilityCheckMiddleware>();
            _applicationHostOptions = applicationHostOptions;
        }

        public async Task Invoke(HttpContext httpContext, IScriptHostManager scriptHostManager)
        {
            if (scriptHostManager.State != ScriptHostState.Offline)
            {
                using (Logger.VerifyingHostAvailabilityScope(_logger, httpContext.TraceIdentifier))
                {
                    Logger.InitiatingHostAvailabilityCheck(_logger);

                    bool hostReady = await scriptHostManager.DelayUntilHostReady();
                    if (!hostReady)
                    {
                        Logger.HostUnavailableAfterCheck(_logger);

                        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await httpContext.Response.WriteAsync("Function host is not running.");

                        return;
                    }

                    Logger.HostAvailabilityCheckSucceeded(_logger);

                    await _next.Invoke(httpContext);
                }
            }
            else
            {
                // host is offline so return the app_offline.htm file content
                var offlineFilePath = Path.Combine(_applicationHostOptions.Value.ScriptPath, ScriptConstants.AppOfflineFileName);
                httpContext.Response.ContentType = "text/html";
                httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await httpContext.Response.SendFileAsync(offlineFilePath);
            }
        }
    }
}