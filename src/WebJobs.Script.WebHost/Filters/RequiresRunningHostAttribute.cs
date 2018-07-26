// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Filter applied to actions that require the host instance to be in a state
    /// where functions can be invoked.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequiresRunningHostAttribute : Attribute, IFilterFactory
    {
        private ObjectFactory _factory;

        public RequiresRunningHostAttribute(int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds)
        {
            TimeoutSeconds = timeoutSeconds;
            PollingIntervalMilliseconds = pollingIntervalMilliseconds;
        }

        public int TimeoutSeconds { get; }

        public int PollingIntervalMilliseconds { get; }

        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            if (_factory == null)
            {
               _factory = ActivatorUtilities.CreateFactory(typeof(RunningHostCheckAttribute), Type.EmptyTypes);
            }

            var hostCheckFilter = (RunningHostCheckAttribute)_factory(serviceProvider, null);

            hostCheckFilter.PollingIntervalMilliseconds = PollingIntervalMilliseconds;
            hostCheckFilter.TimeoutSeconds = TimeoutSeconds;

            return hostCheckFilter;
        }

        private class RunningHostCheckAttribute : ActionFilterAttribute
        {
            private readonly IScriptHostManager _hostManager;

            public RunningHostCheckAttribute(IScriptHostManager hostManager)
            {
                _hostManager = hostManager;
            }

            public int TimeoutSeconds { get; internal set; }

            public int PollingIntervalMilliseconds { get; internal set; }

            public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
            {
                // If the host is not ready, we'll wait a bit for it to initialize.
                // This might happen if http requests come in while the host is starting
                // up for the first time, or if it is restarting.
                await DelayUntilHostReady(_hostManager, TimeoutSeconds, PollingIntervalMilliseconds);

                await next();
            }

            internal static async Task<bool> DelayUntilHostReady(IScriptHostManager hostManager, int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds, bool throwOnFailure = true)
            {
                bool CanInvoke()
                {
                    return hostManager.State == ScriptHostState.Running || hostManager.State == ScriptHostState.Initialized;
                }

                await Utility.DelayAsync(timeoutSeconds, pollingIntervalMilliseconds, () =>
                {
                    return !CanInvoke() &&
                            hostManager.State != ScriptHostState.Error;
                });

                bool hostReady = CanInvoke();

                if (throwOnFailure && !hostReady)
                {
                    throw new HttpException(HttpStatusCode.ServiceUnavailable, "Function host is not running.");
                }

                return hostReady;
            }
        }
    }
}