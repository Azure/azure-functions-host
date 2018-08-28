// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Filter applied to actions that require the host instance to be in a state
    /// where functions can be invoked.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequiresRunningHostAttribute : WaitForHostAttribute
    {
        public RequiresRunningHostAttribute(
            int timeoutSeconds = ScriptConstants.HostTimeoutSeconds,
            int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds) : base(timeoutSeconds, pollingIntervalMilliseconds, false)
        {
        }
    }

    /// <remarks>
    /// Filter applied to actions that might return different results based on whether the host has started or not and the client might want to request to wait (up to some server specified timeout) for the host to start.
    /// Do not use this on an API that requires a running host to execute successfully.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ClientCanRequestRunningHost : WaitForHostAttribute
    {
        public ClientCanRequestRunningHost(
            int timeoutSeconds = ScriptConstants.HostTimeoutSeconds,
            int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds) : base(timeoutSeconds, pollingIntervalMilliseconds, true)
        {
        }
    }

    public abstract class WaitForHostAttribute : Attribute, IFilterFactory
    {
        private ObjectFactory _factory;

        protected WaitForHostAttribute(int timeoutSeconds, int pollingIntervalMilliseconds, bool allowClientControlledWait)
        {
            TimeoutSeconds = timeoutSeconds;
            PollingIntervalMilliseconds = pollingIntervalMilliseconds;
            AllowClientControlledWait = allowClientControlledWait;
        }

        public int TimeoutSeconds { get; }

        public int PollingIntervalMilliseconds { get; }

        public bool IsReusable => false;

        /// <summary>
        /// Gets or sets a value indicating whether the client can control the behavior of waiting for the host.
        /// </summary>
        public bool AllowClientControlledWait { get; set; }

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            if (_factory == null)
            {
               _factory = ActivatorUtilities.CreateFactory(typeof(RunningHostCheckAttribute), Type.EmptyTypes);
            }

            var hostCheckFilter = (RunningHostCheckAttribute)_factory(serviceProvider, null);

            hostCheckFilter.PollingIntervalMilliseconds = PollingIntervalMilliseconds;
            hostCheckFilter.TimeoutSeconds = TimeoutSeconds;
            hostCheckFilter.AllowClientControlledWait = AllowClientControlledWait;

            return hostCheckFilter;
        }

        private class RunningHostCheckAttribute : ActionFilterAttribute
        {
            private const string WaitForHostQueryStringKey = "waitForHost";
            private readonly IScriptHostManager _hostManager;

            public RunningHostCheckAttribute(IScriptHostManager hostManager)
            {
                _hostManager = hostManager;
            }

            public int TimeoutSeconds { get; internal set; }

            public int PollingIntervalMilliseconds { get; internal set; }

            public bool AllowClientControlledWait { get; internal set; }

            public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
            {
                if (AllowClientControlledWait == false)
                {
                    // Default case. In this mode, always wait for the host to initialize and fail if the host failed to start in the allotted time.
                    bool hostReady = await _hostManager.DelayUntilHostReady(TimeoutSeconds, PollingIntervalMilliseconds);

                    if (!hostReady)
                    {
                        throw new HttpException(HttpStatusCode.ServiceUnavailable, "Function host is not running.");
                    }
                }
                else
                {
                    // In this mode, only wait if the client requested us to do so via the query parameter.
                    // We do not care about the host status.
                    if (actionContext.HttpContext.Request.Query.TryGetValue(WaitForHostQueryStringKey, out StringValues value)
                        && string.Compare("1", value) == 0)
                    {
                        await _hostManager.DelayUntilHostReady(TimeoutSeconds, PollingIntervalMilliseconds);
                    }
                }

                await next();
            }
        }
    }
}