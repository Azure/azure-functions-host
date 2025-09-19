// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    // This class resolves worker configurations by scanning the "workers" directory within the Host for worker config files.
    internal sealed class DefaultWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IFileSystem _fileSystem;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;

        public DefaultWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                    IMetricsLogger metricsLogger,
                                                    IFileSystem fileSystem,
                                                    IWorkerProfileManager workerProfileManager,
                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _workerConfigurationResolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));
            ArgumentNullException.ThrowIfNull(_workerConfigurationResolverOptions.CurrentValue);
        }

        public Dictionary<string, RpcWorkerConfig> GetWorkerConfigs()
        {
            _logger.DefaultWorkersDirectoryPath(_workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath);

            return ResolveWorkerConfigsFromWithinHost(_workerConfigurationResolverOptions.CurrentValue,
                                                            _logger,
                                                            _fileSystem,
                                                            _metricsLogger,
                                                            _systemRuntimeInformation,
                                                            _profileManager);
        }

        internal static Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromWithinHost(WorkerConfigurationResolverOptions resolverOptions,
                                                                                    ILogger logger,
                                                                                    IFileSystem fileSystem,
                                                                                    IMetricsLogger metricsLogger,
                                                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                                                    IWorkerProfileManager profileManager,
                                                                                    Dictionary<string, RpcWorkerConfig> availableRuntimeToConfigMap = null)
        {
            // It could be partially filled by dynamic worker config resolver, which searches for worker configs in probing paths.
            // This applies to scenarios such as multi-language worker environment and placeholder mode where some worker configs are found in probing paths, while remaining configs will be loaded from the default path within the Host.
            availableRuntimeToConfigMap = availableRuntimeToConfigMap ?? new Dictionary<string, RpcWorkerConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var workerPath in fileSystem.Directory.EnumerateDirectories(resolverOptions.WorkersRootDirPath))
            {
                var workerDir = Path.GetFileName(workerPath);

                if (availableRuntimeToConfigMap.ContainsKey(workerDir) || WorkerConfigurationHelper.ShouldSkipWorkerDirectory(resolverOptions.WorkerRuntime, Path.GetFileName(workerPath), resolverOptions.IsMultiLanguageWorkerEnvironment, resolverOptions.IsPlaceholderModeEnabled))
                {
                    continue;
                }

                (var workerDescription, var workerConfigJson) = WorkerConfigurationHelper.GetWorkerConfigAndDescription(workerPath, profileManager, resolverOptions.WorkerDescriptionOverrides, logger);
                if (workerDescription is null || WorkerConfigurationHelper.ShouldSkipDisabledWorker(workerDescription, logger))
                {
                    continue;
                }

                var workerConfig = WorkerConfigurationHelper.BuildWorkerConfig(resolverOptions, workerPath, workerConfigJson, workerDescription, metricsLogger, logger, systemRuntimeInformation);
                if (workerConfig is not null)
                {
                    availableRuntimeToConfigMap[workerDir] = workerConfig;
                }

                if (!resolverOptions.IsMultiLanguageWorkerEnvironment &&
                    !resolverOptions.IsPlaceholderModeEnabled &&
                    !string.IsNullOrWhiteSpace(resolverOptions.WorkerRuntime) &&
                    availableRuntimeToConfigMap.ContainsKey(resolverOptions.WorkerRuntime))
                {
                    break;
                }
            }

            return availableRuntimeToConfigMap;
        }
    }
}
