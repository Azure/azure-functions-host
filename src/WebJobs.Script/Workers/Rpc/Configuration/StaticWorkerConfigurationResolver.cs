// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal class StaticWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public StaticWorkerConfigurationResolver(IConfiguration configuration, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration;
        }

        public List<string> GetWorkerConfigs()
        {
            string workersDirPath = WorkerConfigurationHelper.GetWorkersDirPath(_configuration);
            _logger.LogDebug("Workers Directory set to: {WorkersDirPath}", workersDirPath);

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
