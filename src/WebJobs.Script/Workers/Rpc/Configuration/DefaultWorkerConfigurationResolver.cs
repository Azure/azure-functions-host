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
        private readonly IOptions<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;

        public DefaultWorkerConfigurationResolver(ILogger logger, IOptions<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workerConfigurationResolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
        }

        public List<string> GetWorkerConfigPaths()
        {
            var workersDirPath = WorkerConfigurationHelper.GetWorkersDirPath(_workerConfigurationResolverOptions.Value.LanguageSection);
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
