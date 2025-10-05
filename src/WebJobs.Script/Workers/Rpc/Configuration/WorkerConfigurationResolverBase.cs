// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal abstract class WorkerConfigurationResolverBase : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _resolverOptions;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IFileSystem _fileSystem;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;

        public WorkerConfigurationResolverBase(ILoggerFactory loggerFactory,
                                                    IMetricsLogger metricsLogger,
                                                    IFileSystem fileSystem,
                                                    IWorkerProfileManager workerProfileManager,
                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _resolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));
            ArgumentNullException.ThrowIfNull(_resolverOptions.CurrentValue);
        }

        protected ILogger Logger => _logger;

        protected WorkerConfigurationResolverOptions WorkerResolverOptions => _resolverOptions.CurrentValue;

        protected IMetricsLogger MetricsLogger => _metricsLogger;

        protected IWorkerProfileManager ProfileManager => _profileManager;

        protected IFileSystem FileSystem => _fileSystem;

        protected ISystemRuntimeInformation SystemRuntimeInformation => _systemRuntimeInformation;

        public abstract Dictionary<string, RpcWorkerConfig> GetWorkerConfigs();

        internal void ResolveWorkerConfigsFromWithinHost(Dictionary<string, RpcWorkerConfig> availableWorkerRuntimeToConfigMap)
        {
            // `availableWorkerRuntimeToConfigMap` could be partially filled by DynamicWorkerConfigurationResolver, which searches for worker configs in probing paths.
            // This applies to scenarios such as multi-language worker environment and placeholder mode where some worker configs are found in probing paths, while remaining configs will be loaded from the default path within the Host.
            ArgumentNullException.ThrowIfNull(availableWorkerRuntimeToConfigMap);

            foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(WorkerResolverOptions.WorkersRootDirPath))
            {
                var workerDir = Path.GetFileName(workerPath);

                if (availableWorkerRuntimeToConfigMap.ContainsKey(workerDir) || WorkerConfigurationHelper.ShouldSkipWorkerDirectory(WorkerResolverOptions.WorkerRuntime, workerDir, WorkerResolverOptions.IsMultiLanguageWorkerEnvironment, WorkerResolverOptions.IsPlaceholderModeEnabled))
                {
                    continue;
                }

                (var workerDescription, var workerConfigJson) = WorkerConfigurationHelper.GetWorkerDescriptionAndConfig(workerPath, _profileManager, WorkerResolverOptions.WorkerDescriptionOverrides, _logger);
                if (workerDescription is null || WorkerConfigurationHelper.IsWorkerDescriptionDisabled(workerDescription, _logger))
                {
                    continue;
                }

                var workerConfig = WorkerConfigurationHelper.BuildWorkerConfig(WorkerResolverOptions, workerPath, workerConfigJson, workerDescription, _metricsLogger, _logger, _systemRuntimeInformation);
                if (workerConfig is not null)
                {
                    availableWorkerRuntimeToConfigMap[workerDir] = workerConfig;
                }
            }
        }
    }
}
