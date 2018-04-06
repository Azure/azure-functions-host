// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class WorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;

        public WorkerConfigFactory(IConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public IEnumerable<WorkerConfig> GetConfigs(IEnumerable<IWorkerProvider> providers)
        {
            foreach (var provider in providers)
            {
                var description = provider.GetDescription();
                var languageSection = _config.GetSection($"workers:{description.Language}");

                // Can override the path we load from, or we use the default path from where we loaded the config
                var workerPath = languageSection.GetSection("path").Value ?? Path.Combine(provider.GetWorkerDirectoryPath(), description.DefaultWorkerPath);

                var arguments = new ArgumentsDescription()
                {
                    ExecutablePath = description.DefaultExecutablePath,
                    WorkerPath = workerPath
                };

                if (provider.TryConfigureArguments(arguments, _config, _logger))
                {
                    yield return new WorkerConfig()
                    {
                        Description = description,
                        Arguments = arguments
                    };
                }
                else
                {
                    _logger.LogTrace($"Could not configure language worker {description.Language}.");
                }
            }
        }
    }
}
