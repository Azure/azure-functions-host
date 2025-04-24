// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Proxy;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Extensions.Http
{
    public class ScriptRouteHandler : IWebJobsRouteHandler
    {
        private readonly IScriptJobHost _scriptHost;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IEnvironment _environment;
        private readonly bool _isProxy;
        private readonly bool _isWarmup;
        private static int _warmupExecuted;
        private readonly ConcurrentDictionary<string, FunctionDescriptor> _functionMap = new ConcurrentDictionary<string, FunctionDescriptor>(System.StringComparer.OrdinalIgnoreCase);

        public ScriptRouteHandler(ILoggerFactory loggerFactory, IScriptJobHost scriptHost, IEnvironment environment, bool isProxy, bool isWarmup = false)
        {
            _scriptHost = scriptHost;
            _loggerFactory = loggerFactory;
            _environment = environment;
            _isProxy = isProxy;
            _isWarmup = isWarmup;
        }

        public Task InvokeAsync(HttpContext context, string functionName)
        {
            if (_isProxy)
            {
                ProxyFunctionExecutor proxyFunctionExecutor = new ProxyFunctionExecutor(_scriptHost);
                context.Items.TryAdd(ScriptConstants.AzureProxyFunctionExecutorKey, proxyFunctionExecutor);
            }
            else if (_isWarmup)
            {
                // warmup function will get executed just once for the process.
                if (Interlocked.CompareExchange(ref _warmupExecuted, 1, 0) != 0)
                {
                    return Task.CompletedTask;
                }
            }

            var descriptor = _functionMap.GetOrAdd(functionName, (name) =>
            {
                return _scriptHost.Functions.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            });

            if (_isWarmup && descriptor == null)
            {
                // TODO: further optimization, If there is no warmup trigger provided we should call a simple warmup function for the given language of the function app.
                return Task.CompletedTask;
            }

            var executionFeature = new FunctionExecutionFeature(_scriptHost, descriptor, _environment, _loggerFactory);
            context.Features.Set<IFunctionExecutionFeature>(executionFeature);

            return Task.CompletedTask;
        }
    }
}