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
    internal sealed class DefaultWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                IMetricsLogger metricsLogger,
                                                IFileSystem fileSystem,
                                                IWorkerProfileManager workerProfileManager,
                                                ISystemRuntimeInformation systemRuntimeInformation,
                                                IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
                        : WorkerConfigurationResolverBase(loggerFactory, metricsLogger, fileSystem, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
    {
        public override Dictionary<string, RpcWorkerConfig> GetWorkerConfigs()
        {
            Logger.DefaultWorkersDirectoryPath(WorkerResolverOptions.WorkersRootDirPath);

            var workerRuntimeToConfigMap = new Dictionary<string, RpcWorkerConfig>(StringComparer.OrdinalIgnoreCase);

            ResolveWorkerConfigsFromWithinHost(workerRuntimeToConfigMap);

            return workerRuntimeToConfigMap;
        }
    }
}
