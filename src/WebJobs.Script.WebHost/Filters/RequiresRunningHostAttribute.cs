﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;

            public RunningHostCheckAttribute(IScriptHostManager hostManager, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions)
            {
                _hostManager = hostManager;
                _applicationHostOptions = applicationHostOptions;
            }

            public int TimeoutSeconds { get; internal set; }

            public int PollingIntervalMilliseconds { get; internal set; }

            public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
            {
                await actionContext.HttpContext.WaitForRunningHostAsync(_hostManager, _applicationHostOptions.CurrentValue, TimeoutSeconds, PollingIntervalMilliseconds, next);
            }
        }
    }
}