// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class RpcWorkerConfigFactory
    {
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly string _workerRuntime;
        private readonly IEnvironment _environment;
        private readonly IWorkerConfigurationResolver _workerConfigurationResolver;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;
        private Dictionary<string, RpcWorkerConfig> _workerDescriptionDictionary = new Dictionary<string, RpcWorkerConfig>();

        public RpcWorkerConfigFactory(ILogger logger,
                                        ISystemRuntimeInformation systemRuntimeInfo,
                                        IEnvironment environment,
                                        IMetricsLogger metricsLogger,
                                        IWorkerProfileManager workerProfileManager,
                                        IWorkerConfigurationResolver workerConfigurationResolver,
                                        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            _workerConfigurationResolver = workerConfigurationResolver ?? throw new ArgumentNullException(nameof(workerConfigurationResolver));
            _workerConfigurationResolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
        }

        public IList<RpcWorkerConfig> GetConfigs()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.GetConfigs))
            {
                BuildWorkerProviderDictionary();
                return _workerDescriptionDictionary.Values.ToList();
            }
        }

        internal void BuildWorkerProviderDictionary()
        {
            _workerDescriptionDictionary = _workerConfigurationResolver.GetWorkerConfigs();
            AddProvidersFromAppSettings();
        }

        internal void AddProvidersFromAppSettings()
        {
            var languageWorkersSettings = _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings;
            foreach (var kvp in languageWorkersSettings)
            {
                var language = kvp.Key;
                var workerDescription = kvp.Value;
                if (!string.IsNullOrEmpty(workerDescription?.WorkerDirectory))
                {
                    _workerDescriptionDictionary.Remove(language);
                    var rpcWorkerConfig = WorkerConfigurationHelper.AddProvider(
                                                                        _workerConfigurationResolverOptions.CurrentValue,
                                                                        workerDescription.WorkerDirectory,
                                                                        _metricsLogger,
                                                                        _logger,
                                                                        _systemRuntimeInformation,
                                                                        _profileManager);
                    if (rpcWorkerConfig is not null)
                    {
                        _workerDescriptionDictionary[language] = rpcWorkerConfig;
                    }
                }
            }
        }
    }
}
