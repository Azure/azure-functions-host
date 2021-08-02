// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionMetadataProviderFactory : IFunctionMetadataProviderFactory
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILoggerFactory _loggerFactory;
        private IFunctionMetadataProvider _hostFunctionMetadataProvider;
        private IFunctionMetadataProvider _workerFunctionMetadataProvider;
        private IFunctionInvocationDispatcher _dispatcher;

        public FunctionMetadataProviderFactory(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, IFunctionInvocationDispatcherFactory dispatcherFactory)
        {
            _applicationHostOptions = applicationHostOptions;
            _metricsLogger = metricsLogger;
            _loggerFactory = loggerFactory;
            _dispatcher = dispatcherFactory.GetFunctionDispatcher();
        }

        public void Create()
        {
            _workerFunctionMetadataProvider = new WorkerFunctionMetadataProvider(_loggerFactory.CreateLogger<WorkerFunctionMetadataProvider>(), _dispatcher);
            _hostFunctionMetadataProvider = new HostFunctionMetadataProvider(_applicationHostOptions, _loggerFactory.CreateLogger<HostFunctionMetadataProvider>(), _metricsLogger);
        }

        public IFunctionMetadataProvider GetProvider(IList<RpcWorkerConfig> workerConfigs)
        {
            var workerRuntime = SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            if (workerRuntime == null)
            {
                return _hostFunctionMetadataProvider;
            }
            RpcWorkerConfig workerConfig = workerConfigs.FirstOrDefault(
                    config => workerRuntime.Equals(config.Description.Language, StringComparison.OrdinalIgnoreCase));

            // return host-indexing provider if placeholder mode is enabled, feature flag is disabled, or worker is not capable of indexing
            if (SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode) == "1" ||
                    !FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableWorkerIndexing) ||
                    (workerConfig == null || workerConfig.Description.WorkerIndexing == null || !workerConfig.Description.WorkerIndexing.Equals("true")))
            {
                return _hostFunctionMetadataProvider;
            }
            return _workerFunctionMetadataProvider;
        }
    }
}
