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
            var workersRootDirPath = _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath;
            _logger.DefaultWorkersDirectoryPath(workersRootDirPath);

            var workerConfigPaths = new Dictionary<string, RpcWorkerConfig>();

            foreach (var workerDir in _fileSystem.Directory.EnumerateDirectories(workersRootDirPath))
            {
                string workerConfigPath = _fileSystem.Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);

                if (_fileSystem.File.Exists(workerConfigPath))
                {
                    var config = WorkerConfigurationHelper.AddProvider(workerDir,
                                                        _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                                                        _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                                                        _metricsLogger,
                                                        workerDir,
                                                        _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                                                        _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                                                        _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                                                        _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment,
                                                        _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
                                                        _logger,
                                                        SystemRuntimeInformation.Instance,
                                                        _profileManager);

                    workerConfigPaths[workerDir] = config;
                }
            }

            return new WorkerConfigurationInfo(
                WorkersRootDirPath: _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                WorkerConfigPaths: workerConfigPaths,
                LanguageWorkersSettings: _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                CoreCount: _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
                FWRSetting: _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                FunctionsWorkerProcessCountSettingName: _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                WorkerRuntime: _workerConfigurationResolverOptions.CurrentValue.WorkerRuntime,
                Placeholder: _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                Multilanfg: _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment);
        }
    }
}
