// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration.WorkerConfigurationHelper;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// Base class for worker configuration resolvers.
    /// </summary>
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

        /// <summary>
        /// Resolves worker configurations by scanning the "workers" directory within the Host for worker config files.
        /// </summary>
        internal void ResolveWorkerConfigsFromWithinHost(Dictionary<string, RpcWorkerConfig> workerRuntimeToConfigMap)
        {
            ArgumentNullException.ThrowIfNull(workerRuntimeToConfigMap);

            foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(WorkerResolverOptions.WorkersRootDirPath))
            {
                AddProvider(WorkerResolverOptions, workerPath, _metricsLogger, _profileManager, _logger, _systemRuntimeInformation, workerRuntimeToConfigMap);
            }
        }
    }
}
