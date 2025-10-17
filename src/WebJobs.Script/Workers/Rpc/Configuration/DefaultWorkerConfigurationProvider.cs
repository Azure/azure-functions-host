// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// This class resolves worker configurations by scanning the "workers" directory within the Host for worker config files.
    /// </summary>
    internal sealed class DefaultWorkerConfigurationProvider : WorkerConfigurationProviderBase
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        public DefaultWorkerConfigurationProvider(
                        ILoggerFactory loggerFactory,
                        IMetricsLogger metricsLogger,
                        IFileSystem fileSystem,
                        IWorkerProfileManager workerProfileManager,
                        ISystemRuntimeInformation systemRuntimeInformation,
                        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
                        : base(metricsLogger, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _fileSystem = fileSystem;
        }

        public override ILogger Logger { get => _logger; }

        public override int Priority { get => 2; }

        public override void PopulateWorkerConfigs(Dictionary<string, RpcWorkerConfig> workerRuntimeToConfigMap)
        {
            Logger.DefaultWorkersDirectoryPath(WorkerResolverOptions.WorkersRootDirPath);

            // Resolves worker configurations by scanning the "workers" directory within the Host.
            foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(WorkerResolverOptions.WorkersRootDirPath))
            {
                var workerDirName = _fileSystem.Path.GetFileName(workerPath);
                AddProvider(WorkerResolverOptions, workerDirName, workerPath, workerRuntimeToConfigMap);
            }
        }
    }
}
