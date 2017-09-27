using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
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
                var languageSection = _config.GetSection(description.Language);

                // get explicit worker path from config, or build relative path from default
                var workerPath = languageSection.GetSection("path").Value;
                if (string.IsNullOrEmpty(workerPath) && !string.IsNullOrEmpty(description.DefaultWorkerPath))
                {
                    workerPath = Path.Combine(_assemblyDir, "workers", description.Language, description.DefaultWorkerPath);
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
