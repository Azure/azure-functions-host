// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
    /// Resolves worker configurations based on explicit worker directory specified via App settings.
    /// </summary>
    internal sealed class ExplicitWorkerConfigurationProvider : WorkerConfigurationProviderBase
    {
        public ExplicitWorkerConfigurationProvider(
            ILoggerFactory loggerFactory,
            IMetricsLogger metricsLogger,
            IFileSystem fileSystem,
            IWorkerProfileManager workerProfileManager,
            ISystemRuntimeInformation systemRuntimeInformation,
            IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
            : base(loggerFactory, metricsLogger, fileSystem, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
        {
        }

        public override int Priority => 1;

        public override void ResolveWorkerConfigs(Dictionary<string, RpcWorkerConfig> workerRuntimeToConfigMap)
        {
            foreach (var (language, workerDescriptionOverride) in WorkerResolverOptions.WorkerDescriptionOverrides)
            {
                if (!string.IsNullOrEmpty(workerDescriptionOverride?.WorkerDirectory))
                {
                    workerRuntimeToConfigMap.Remove(language);
                    AddProvider(
                        WorkerResolverOptions,
                        workerDescriptionOverride.WorkerDirectory,
                        MetricsLogger,
                        ProfileManager,
                        Logger,
                        SystemRuntimeInformation,
                        workerRuntimeToConfigMap);
                }
            }
        }
    }
}
