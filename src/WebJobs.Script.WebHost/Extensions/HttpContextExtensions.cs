// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    internal static class HttpContextExtensions
    {
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
