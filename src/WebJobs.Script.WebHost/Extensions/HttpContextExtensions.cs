// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    internal static class HttpContextExtensions
    {
        public static async Task SetOfflineResponseAsync(this HttpContext httpContext, string scriptPath)
        {
            // host is offline so return the app_offline.htm file content
            var offlineFilePath = Path.Combine(scriptPath, ScriptConstants.AppOfflineFileName);
            httpContext.Response.ContentType = "text/html";
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await httpContext.Response.SendFileAsync(offlineFilePath);
        }
    }
}
