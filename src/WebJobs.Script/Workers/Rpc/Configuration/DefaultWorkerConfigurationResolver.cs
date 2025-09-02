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

            var config = ResolveWorkerConfigsFromWithinHost(_workerConfigurationResolverOptions.CurrentValue.WorkerRuntime,
                                                            output,
                                                            _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                                                            _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                                                            _logger,
                                                            _fileSystem,
                                                            _metricsLogger,
                                                            _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                                                            _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                                                            _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                                                            _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment,
                                                            _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
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

        internal static Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromWithinHost(string workerRuntime,
                                                                                    Dictionary<string, RpcWorkerConfig> runtimeToConfigPathMap,
                                                                                    string path,
                                                                                    ImmutableDictionary<string, string> languageWorkersSettings,
                                                                                    ILogger logger,
                                                                                    IFileSystem fileSystem,
                                                                                    IMetricsLogger metricsLogger,
                                                                                    string functionWorkerRuntimeVersionSettingName,
                                                                                    string functionsWorkerProcessCountSettingName,
                                                                                    bool placeholderModeEnabled,
                                                                                    bool multiLanguageWorkerEnvironment,
                                                                                    int coreCount,
                                                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                                                    IWorkerProfileManager profileManager)
        {
            var fallbackPath = path;

            logger.LogDebug("Searching for worker configs in the fallback directory: {fallbackPath}", fallbackPath);

            foreach (var workerPath in fileSystem.Directory.EnumerateDirectories(fallbackPath))
            {
                string workerDir = Path.GetFileName(workerPath);

                if (runtimeToConfigPathMap.ContainsKey(workerDir) || WorkerConfigurationHelper.ShouldSkipWorkerDirectory(workerRuntime, workerDir, placeholderModeEnabled, multiLanguageWorkerEnvironment))
                {
                    continue;
                }

                string workerConfigPath = Path.Combine(workerPath, RpcWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    var config = WorkerConfigurationHelper.AddProvider(workerPath,
                                                                            path,
                                                                            languageWorkersSettings,
                                                                            metricsLogger,
                                                                            workerDir,
                                                                            functionWorkerRuntimeVersionSettingName,
                                                                            functionsWorkerProcessCountSettingName,
                                                                            placeholderModeEnabled,
                                                                            multiLanguageWorkerEnvironment,
                                                                            coreCount,
                                                                            logger,
                                                                            systemRuntimeInformation,
                                                                            profileManager);

                    runtimeToConfigPathMap[workerDir] = config;
                }

                if (WorkerConfigurationHelper.FoundWorkerConfigPath(workerRuntime, runtimeToConfigPathMap, placeholderModeEnabled, multiLanguageWorkerEnvironment))
                {
                    return runtimeToConfigPathMap;
                }
            }

            return runtimeToConfigPathMap;
        }
    }
}
