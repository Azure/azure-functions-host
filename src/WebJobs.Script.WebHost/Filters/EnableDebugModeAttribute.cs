// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Filter applied to actions that should enable "Debug Mode".
    /// </summary>
    /// <remarks>
    /// This attribute is applied to actions that the Portal/CLI invoke
    /// as part of their normal interactions. This puts the host in debug
    /// mode at the right time. E.g. the portal polls the admin/host/status
    /// when it is running.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class EnableDebugModeAttribute : ActionFilterAttribute
    {
        public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
        {
            var scriptHostManager = (WebScriptHostManager)actionContext.HttpContext.Items[ScriptConstants.AzureFunctionsHostManagerKey];

            scriptHostManager.Instance?.NotifyDebug();

            await next();
        }
    }
}