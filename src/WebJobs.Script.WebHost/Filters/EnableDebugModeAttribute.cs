// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

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
        public override Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            var resolver = actionContext.ControllerContext.Configuration.DependencyResolver;
            var scriptHostManager = resolver.GetService<WebScriptHostManager>();

            scriptHostManager.Instance?.NotifyDebug();

            return Task.CompletedTask;
        }
    }
}