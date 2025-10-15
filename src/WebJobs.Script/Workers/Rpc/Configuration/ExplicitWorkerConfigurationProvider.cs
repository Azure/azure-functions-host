// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
    internal sealed class ExplicitWorkerConfigurationProvider(
                                ILoggerFactory loggerFactory,
                                IMetricsLogger metricsLogger,
                                IWorkerProfileManager workerProfileManager,
                                ISystemRuntimeInformation systemRuntimeInformation,
                                IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
                    : WorkerConfigurationProviderBase(loggerFactory, metricsLogger, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
    {
        public override int Priority => 1;

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
