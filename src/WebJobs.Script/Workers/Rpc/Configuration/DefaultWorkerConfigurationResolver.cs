// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
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

        public DefaultWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                    IMetricsLogger metricsLogger,
                                                    IFileSystem fileSystem,
                                                    IWorkerProfileManager workerProfileManager,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _workerConfigurationResolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
        }

        public WorkerConfigurationInfo GetConfigurationInfo()
        {
            var output = new Dictionary<string, RpcWorkerConfig>();

            var config = ResolveWorkerConfigsFromWithinHost(_workerConfigurationResolverOptions.CurrentValue,
                                                            output,
                                                            _logger,
                                                            _fileSystem,
                                                            _metricsLogger,
                                                            SystemRuntimeInformation.Instance,
                                                            _profileManager);

            return new WorkerConfigurationInfo(
                WorkersRootDirPath: _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                WorkerConfigPaths: output,
                LanguageWorkersSettings: _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                CoreCount: _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
                FWRSetting: _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                FunctionsWorkerProcessCountSettingName: _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                WorkerRuntime: _workerConfigurationResolverOptions.CurrentValue.WorkerRuntime,
                Placeholder: _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                Multilanfg: _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment);
        }

        internal static Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromWithinHost(WorkerConfigurationResolverOptions resolverOptions,
                                                                                    Dictionary<string, RpcWorkerConfig> runtimeToConfigPathMap,
                                                                                    ILogger logger,
                                                                                    IFileSystem fileSystem,
                                                                                    IMetricsLogger metricsLogger,
                                                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                                                    IWorkerProfileManager profileManager)
        {
            var fallbackPath = resolverOptions.WorkersRootDirPath;

            foreach (var workerPath in fileSystem.Directory.EnumerateDirectories(fallbackPath))
            {
                string workerDir = Path.GetFileName(workerPath);

                if (runtimeToConfigPathMap.ContainsKey(workerDir) || WorkerConfigurationHelper.ShouldSkipWorkerDirectory(resolverOptions.WorkerRuntime, workerDir, resolverOptions.IsPlaceholderModeEnabled, resolverOptions.IsMultiLanguageWorkerEnvironment))
                {
                    continue;
                }

                string workerConfigPath = Path.Combine(workerPath, RpcWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    var config = WorkerConfigurationHelper.AddProvider(resolverOptions,
                                                                        workerPath,
                                                                        metricsLogger,
                                                                        logger,
                                                                        systemRuntimeInformation,
                                                                        profileManager);

                    runtimeToConfigPathMap[workerDir] = config;
                }

                if (WorkerConfigurationHelper.FoundWorkerConfigPath(resolverOptions.WorkerRuntime, runtimeToConfigPathMap, resolverOptions.IsPlaceholderModeEnabled, resolverOptions.IsMultiLanguageWorkerEnvironment))
                {
                    return runtimeToConfigPathMap;
                }
            }

            return runtimeToConfigPathMap;
        }
    }
}
