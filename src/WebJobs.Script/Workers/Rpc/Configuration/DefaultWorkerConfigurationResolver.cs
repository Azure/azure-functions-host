// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    // This class resolves worker configurations by scanning the "workers" directory within the Host for worker config files.
    internal sealed class DefaultWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;

        public DefaultWorkerConfigurationResolver(ILoggerFactory loggerFactory, IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            _logger = loggerFactory is not null ? loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig) : throw new ArgumentNullException(nameof(loggerFactory));
            _workerConfigurationResolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
        }

        public List<string> GetWorkerConfigPaths()
        {
            var workersDirPath = _workerConfigurationResolverOptions.CurrentValue.WorkersDirPath;
            _logger.LogDebug("Workers Directory set to: {workersDirPath}", workersDirPath);

            List<string> workerConfigs = new();

            foreach (var workerDir in Directory.EnumerateDirectories(workersDirPath))
            {
                string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);

                if (File.Exists(workerConfigPath))
                {
                    workerConfigs.Add(workerDir);
                }
            }
            return workerConfigs;
        }
    }
}