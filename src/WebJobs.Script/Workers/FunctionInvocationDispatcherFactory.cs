// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class FunctionInvocationDispatcherFactory : IFunctionInvocationDispatcherFactory
    {
        private readonly IFunctionInvocationDispatcher _functionDispatcher;

        public FunctionInvocationDispatcherFactory(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IApplicationLifetime applicationLifetime,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IHttpWorkerChannelFactory httpWorkerChannelFactory,
            IRpcWorkerChannelFactory rpcWorkerChannelFactory,
            IOptions<HttpWorkerOptions> httpWorkerOptions,
            IOptionsMonitor<LanguageWorkerOptions> workerOptions,
            IEnvironment environment,
            IWebHostRpcWorkerChannelManager webHostLanguageWorkerChannelManager,
            IJobHostRpcWorkerChannelManager jobHostLanguageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IRpcFunctionInvocationDispatcherLoadBalancer functionDispatcherLoadBalancer,
            IOptions<WorkerConcurrencyOptions> workerConcurrencyOptions,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
            IHostMetrics hostMetrics)
        {
            if (httpWorkerOptions.Value == null)
            {
                throw new ArgumentNullException(nameof(httpWorkerOptions.Value));
            }

            if (httpWorkerOptions.Value.Description != null)
            {
                _functionDispatcher = new HttpFunctionInvocationDispatcher(scriptHostOptions, metricsLogger, applicationLifetime, eventManager, loggerFactory, httpWorkerChannelFactory);
                return;
            }
            _functionDispatcher = new RpcFunctionInvocationDispatcher(scriptHostOptions,
                metricsLogger,
                environment,
                applicationLifetime,
                eventManager,
                loggerFactory,
                rpcWorkerChannelFactory,
                workerOptions,
                webHostLanguageWorkerChannelManager,
                jobHostLanguageWorkerChannelManager,
                managedDependencyOptions,
                functionDispatcherLoadBalancer,
                workerConcurrencyOptions,
                hostingConfigOptions,
                hostMetrics);
        }

        public IFunctionInvocationDispatcher GetFunctionDispatcher() => _functionDispatcher;
    }
}
