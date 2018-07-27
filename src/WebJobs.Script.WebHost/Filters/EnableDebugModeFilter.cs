// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc.Filters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Filter applied to actions that should enable "Debug Mode".
    /// </summary>
    /// <remarks>
    /// This filter is applied to actions that the Portal/CLI invoke
    /// as part of their normal interactions. This puts the host in debug
    /// mode at the right time. E.g. the portal polls the admin/host/status
    /// when it is running.
    /// </remarks>
    public sealed class EnableDebugModeFilter : IActionFilter
    {
        private readonly IDebugManager _debugManager;

        public EnableDebugModeFilter(IDebugManager debugManager)
        {
            _debugManager = debugManager;
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            _debugManager?.NotifyDebug();
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
        }
    }
}