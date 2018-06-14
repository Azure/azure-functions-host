// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
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
        private readonly ScriptHostConfiguration _scriptHostConfiguration;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly ILoggerProviderFactory _loggerProviderFactory;

        public ScriptHostFactory(
         IScriptHostEnvironment environment,
         IOptions<JobHostOptions> options,
         IJobHostContextFactory jobHostContextFactory,
         IConnectionStringProvider connectionStringProvider,
         IDistributedLockManager distributedLockManager,
         IScriptEventManager eventManager,
         ScriptHostConfiguration scriptHostConfiguration,
         ScriptSettingsManager settingsManager,
         ILoggerProviderFactory loggerProviderFactory)
        {
            _environment = environment;
            _options = options;
            _jobHostContextFactory = jobHostContextFactory;
            _connectionStringProvider = connectionStringProvider;
            _distributedLockManager = distributedLockManager;
            _eventManager = eventManager;
            _scriptHostConfiguration = scriptHostConfiguration;
            _settingsManager = settingsManager;
            _loggerProviderFactory = loggerProviderFactory;
        }

        public ScriptHost Create()
        {
            return new ScriptHost(_environment, _options, _jobHostContextFactory, _connectionStringProvider, _distributedLockManager,
                _eventManager, _scriptHostConfiguration, _settingsManager, _loggerProviderFactory);
        }
    }
}
