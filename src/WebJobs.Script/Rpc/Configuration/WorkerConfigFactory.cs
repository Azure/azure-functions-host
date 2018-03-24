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
        private readonly string _assemblyDir;

        public WorkerConfigFactory(IConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            _assemblyDir = Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath);
        }

        public IEnumerable<WorkerConfig> GetConfigs(IEnumerable<IWorkerProvider> providers)
        {
            foreach (var provider in providers)
            {
                var description = provider.GetDescription();
                var languageSection = _config.GetSection($"workers:{description.Language}");

                // Resolve worker path
                // 1. If workers.{language}.path is set, use that explicitly
                // 2. If workers.path is set, use that as the base directory + language + default path
                // 3. Else, use the default workers directory

                // get explicit worker path from config, or build relative path from default
                var workerPath = languageSection.GetSection("path").Value;
                if (string.IsNullOrEmpty(workerPath))
                {
                    var baseWorkerPath = !string.IsNullOrEmpty(_config.GetSection("workers:path").Value) ?
                            _config.GetSection("workers:path").Value :
                            Path.Combine(_assemblyDir, "workers");
                    workerPath = Path.Combine(baseWorkerPath, description.Language.ToLower(), description.DefaultWorkerPath);
                }

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
                    _logger.LogError($"Could not configure language worker {description.Language}.");
                }
            }
        }
    }
}
