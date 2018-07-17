// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class ScriptHostFactory : IScriptHostFactory
    {
        private readonly IScriptHostEnvironment _environment;
        private readonly IOptions<JobHostOptions> _options;
        private readonly IJobHostContextFactory _jobHostContextFactory;
        private readonly IConnectionStringProvider _connectionStringProvider;
        private readonly IDistributedLockManager _distributedLockManager;
        private readonly IScriptEventManager _eventManager;
        private readonly IOptions<ScriptHostOptions> _scriptOptions;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILoggerProviderFactory _loggerProviderFactory;
        private readonly IFunctionMetadataManager _functionMetadataManager;
        private readonly IMetricsLogger _metricsLogger;

        public ScriptHostFactory(
         IScriptHostEnvironment environment,
         IOptions<JobHostOptions> options,
         IOptions<ScriptHostOptions> scriptOptions,
         IJobHostContextFactory jobHostContextFactory,
         IConnectionStringProvider connectionStringProvider,
         IDistributedLockManager distributedLockManager,
         IScriptEventManager eventManager,
         ScriptSettingsManager settingsManager,
         ILoggerFactory loggerFactory,
         ILoggerProviderFactory loggerProviderFactory,
         IFunctionMetadataManager functionMetadataManager,
         IMetricsLogger metricsLogger)
        {
            _environment = environment;
            _options = options;
            _jobHostContextFactory = jobHostContextFactory;
            _connectionStringProvider = connectionStringProvider;
            _distributedLockManager = distributedLockManager;
            _eventManager = eventManager;
            _scriptOptions = scriptOptions;
            _settingsManager = settingsManager;
            _loggerFactory = loggerFactory;
            _loggerProviderFactory = loggerProviderFactory;
            _functionMetadataManager = functionMetadataManager;
            _metricsLogger = metricsLogger;
        }

        public ScriptHost Create()
        {
            return new ScriptHost(_environment, _options, _jobHostContextFactory, _connectionStringProvider, _distributedLockManager,
                _eventManager, _loggerFactory, _functionMetadataManager, _metricsLogger, _scriptOptions,  _settingsManager, _loggerProviderFactory);
        }
    }
}
