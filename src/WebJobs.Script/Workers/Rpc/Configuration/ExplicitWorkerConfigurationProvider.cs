// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// Resolves worker configuration based on explicit worker directory specified via App settings.
    /// </summary>
    internal sealed class ExplicitWorkerConfigurationProvider : WorkerConfigurationProviderBase
    {
        private readonly ILogger _logger;

        public ExplicitWorkerConfigurationProvider(
                        ILoggerFactory loggerFactory,
                        IMetricsLogger metricsLogger,
                        IWorkerProfileManager workerProfileManager,
                        ISystemRuntimeInformation systemRuntimeInformation,
                        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
                        : base(metricsLogger, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
        }

        public override int Priority { get => 1; }

        public override ILogger Logger { get => _logger; }

        public override void PopulateWorkerConfigs(Dictionary<string, RpcWorkerConfig> workerRuntimeToConfigMap)
        {
            foreach (var (language, workerDescriptionOverride) in WorkerResolverOptions.WorkerDescriptionOverrides)
            {
                if (!string.IsNullOrEmpty(workerDescriptionOverride?.WorkerDirectory))
                {
                    workerRuntimeToConfigMap.Remove(language);
                    AddProvider(WorkerResolverOptions, language, workerDescriptionOverride.WorkerDirectory, workerRuntimeToConfigMap);
                }
            }
        }
    }
}
