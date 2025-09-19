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
        private readonly IEnvironment _environment;
        private readonly IWorkerConfigurationResolver _workerConfigurationResolver;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _resolverOptions;
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
            var workerDescriptionOverrides = _resolverOptions.CurrentValue.WorkerDescriptionOverrides;

            foreach (var (language, workerDescriptionOverride) in workerDescriptionOverrides)
            {
                if (!string.IsNullOrEmpty(workerDescriptionOverride?.WorkerDirectory))
                {
                    _workerDescriptionDictionary.Remove(language);

                    // Do not skip non-worker directories like the function app payload directory
                    if (WorkerConfigurationHelper.ShouldSkipWorkerDirectory(_resolverOptions.CurrentValue.WorkerRuntime, Path.GetFileName(workerDescriptionOverride.WorkerDirectory), _resolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment, _resolverOptions.CurrentValue.IsPlaceholderModeEnabled)
                        && workerDescriptionOverride.WorkerDirectory.StartsWith(_resolverOptions.CurrentValue.WorkersRootDirPath))
                    {
                        continue;
                    }

                    (var workerDescription, var workerConfigJson) = WorkerConfigurationHelper.GetWorkerConfigAndDescription(workerDescriptionOverride.WorkerDirectory, _profileManager, _resolverOptions.CurrentValue.WorkerDescriptionOverrides, _logger);
                    if (workerDescription is null || WorkerConfigurationHelper.ShouldSkipDisabledWorker(workerDescription, _logger))
                    {
                        continue;
                    }

                    var workerConfig = WorkerConfigurationHelper.BuildWorkerConfig(_resolverOptions.CurrentValue, workerDescriptionOverride.WorkerDirectory, workerConfigJson, workerDescription, _metricsLogger, _logger, _systemRuntimeInformation);
                    if (workerConfig is not null)
                    {
                        _workerDescriptionDictionary[language] = workerConfig;
                    }
                }
            }
        }
    }
}
