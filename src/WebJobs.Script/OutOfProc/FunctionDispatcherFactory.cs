// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc
{
    internal class FunctionDispatcherFactory : IFunctionDispatcherFactory
    {
        private IFunctionDispatcher _functionDispatcher;

        public FunctionDispatcherFactory(IOptions<ScriptJobHostOptions> scriptHostOptions,
            IMetricsLogger metricsLogger,
            IScriptJobHostEnvironment scriptJobHostEnvironment,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IHttpWorkerChannelFactory httpWorkerChannelFactory,
            IRpcWorkerChannelFactory rpcWorkerChannelFactory,
            IOptions<HttpWorkerOptions> httpWorkerOptions,
            IOptions<LanguageWorkerOptions> rpcWorkerOptions,
            IEnvironment environment,
            IWebHostLanguageWorkerChannelManager webHostLanguageWorkerChannelManager,
            IJobHostLanguageWorkerChannelManager jobHostLanguageWorkerChannelManager,
            IOptions<ManagedDependencyOptions> managedDependencyOptions,
            IFunctionDispatcherLoadBalancer functionDispatcherLoadBalancer)
        {
            if (httpWorkerOptions.Value == null)
            {
                throw new ArgumentNullException(nameof(httpWorkerOptions.Value));
            }

            if (httpWorkerOptions.Value.Description != null)
            {
                _functionDispatcher = new HttpFunctionInvocationDispatcher(scriptHostOptions, metricsLogger, scriptJobHostEnvironment, eventManager, loggerFactory, httpWorkerChannelFactory);
                return;
            }
            _functionDispatcher = new RpcFunctionInvocationDispatcher(scriptHostOptions,
                metricsLogger,
                environment,
                scriptJobHostEnvironment,
                eventManager,
                loggerFactory,
                rpcWorkerChannelFactory,
                rpcWorkerOptions,
                webHostLanguageWorkerChannelManager,
                jobHostLanguageWorkerChannelManager,
                managedDependencyOptions,
                functionDispatcherLoadBalancer);
        }

        public IFunctionDispatcher GetFunctionDispatcher() => _functionDispatcher;
    }
}
