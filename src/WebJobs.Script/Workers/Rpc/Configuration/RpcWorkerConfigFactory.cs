// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
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
        private readonly IWorkerConfigurationResolver _workerConfigurationResolver;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _resolverOptions;
        private Dictionary<string, RpcWorkerConfig> _workerDescriptionDictionary = new Dictionary<string, RpcWorkerConfig>();

        public RpcWorkerConfigFactory(ILogger logger,
                                        ISystemRuntimeInformation systemRuntimeInfo,
                                        IMetricsLogger metricsLogger,
                                        IWorkerProfileManager workerProfileManager,
                                        IWorkerConfigurationResolver workerConfigurationResolver,
                                        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workerConfigurationResolver = workerConfigurationResolver ?? throw new ArgumentNullException(nameof(workerConfigurationResolver));
            _resolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
            ArgumentNullException.ThrowIfNull(_resolverOptions.CurrentValue);
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
            foreach (var (language, workerDescriptionOverride) in _resolverOptions.CurrentValue.WorkerDescriptionOverrides)
            {
                if (!string.IsNullOrEmpty(workerDescriptionOverride?.WorkerDirectory))
                {
                    _workerDescriptionDictionary.Remove(language);
                    WorkerConfigurationHelper.AddProvider(_resolverOptions.CurrentValue, workerDescriptionOverride.WorkerDirectory, _metricsLogger, _profileManager, _logger, _systemRuntimeInformation, _workerDescriptionDictionary);
                }
            }
        }
    }
}
