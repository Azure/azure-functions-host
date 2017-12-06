// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Filter applied to actions that require the host instance to be in a state
    /// where functions can be invoked.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequiresRunningHostAttribute : ActionFilterAttribute
    {
        public RequiresRunningHostAttribute(int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds)
        {
            TimeoutSeconds = timeoutSeconds;
            PollingIntervalMilliseconds = pollingIntervalMilliseconds;
        }

        public int TimeoutSeconds { get; }

        public int PollingIntervalMilliseconds { get; }

        public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
        {
            var scriptHostManager = (WebScriptHostManager)actionContext.HttpContext.Items[ScriptConstants.AzureFunctionsHostManagerKey];

            // If the host is not ready, we'll wait a bit for it to initialize.
            // This might happen if http requests come in while the host is starting
            // up for the first time, or if it is restarting.
            await scriptHostManager.DelayUntilHostReady(TimeoutSeconds, PollingIntervalMilliseconds);
        }
    }
}