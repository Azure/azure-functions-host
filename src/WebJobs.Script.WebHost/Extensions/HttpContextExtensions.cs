// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Script.Extensions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    internal static class HttpContextExtensions
    {
        public static async Task WaitForRunningHostAsync(this HttpContext httpContext, IScriptHostManager hostManager, ScriptApplicationHostOptions applicationHostOptions, int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds, ActionExecutionDelegate next = null)
        {
            if (hostManager.State == ScriptHostState.Offline)
            {
                await httpContext.SetOfflineResponseAsync(applicationHostOptions.ScriptPath);
            }
            else
            {
                // If the host is not ready, we'll wait a bit for it to initialize.
                // This might happen if http requests come in while the host is starting
                // up for the first time, or if it is restarting.
                bool hostReady = await hostManager.DelayUntilHostReady(timeoutSeconds, pollingIntervalMilliseconds);

                if (!hostReady)
                {
                    throw new HttpException(HttpStatusCode.ServiceUnavailable, "Function host is not running.");
                }

                if (next != null)
                {
                    await next();
                }
            }
        }

        public static async Task SetOfflineResponseAsync(this HttpContext httpContext, string scriptPath)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            if (!httpContext.Request.IsAppServiceInternalRequest())
            {
                // host is offline so return the app_offline.htm file content
                var offlineFilePath = Path.Combine(scriptPath, ScriptConstants.AppOfflineFileName);
                httpContext.Response.ContentType = "text/html";
                await httpContext.Response.SendFileAsync(offlineFilePath);
            }
        }
    }
}
