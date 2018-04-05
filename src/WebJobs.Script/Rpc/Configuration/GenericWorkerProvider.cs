// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class GenericWorkerProvider : IWorkerProvider
    {
        private WorkerDescription _workerDescription;
        private List<string> _arguments;
        private string _pathToWorkerDir;

        public GenericWorkerProvider(WorkerDescription workerDescription, List<string> arguments, string pathToWorkerDir)
        {
            _workerDescription = workerDescription ?? throw new ArgumentNullException(nameof(workerDescription));
            _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
            _pathToWorkerDir = pathToWorkerDir ?? throw new ArgumentNullException(nameof(pathToWorkerDir));
        }

        public WorkerDescription GetDescription()
        {
            return this._workerDescription;
        }

        public bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            args.ExecutableArguments.AddRange(_arguments);
            return true;
        }

        public string GetWorkerDirectoryPath()
        {
            return _pathToWorkerDir;
        }

        public static List<IWorkerProvider> ReadWorkerProviderFromConfig(ScriptHostConfiguration config, ILogger logger, ScriptSettingsManager settingsManager = null, string language = null)
        {
            var providers = new List<IWorkerProvider>();
            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            var assemblyDir = Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath);

            var workerDirPath = settingsManager.Configuration.GetSection("workers:config:path").Value ?? Path.Combine(assemblyDir, ScriptConstants.DefaultWorkersDirectoryName);

            foreach (var workerDir in Directory.EnumerateDirectories(workerDirPath))
            {
                try
                {
                    // check if worker config exists
                    string workerConfigPath = Path.Combine(workerDir, ScriptConstants.WorkerConfigFileName); // TODO: Move to constant
                    if (!File.Exists(workerConfigPath))
                    {
                        // not a worker directory
                        continue;
                    }

                    string json = File.ReadAllText(workerConfigPath);
                    JObject workerConfig = JObject.Parse(json);

                    WorkerDescription description = workerConfig.Property("Description").Value.ToObject<WorkerDescription>();
                    // var workerSettings = settingsManager.Configuration.GetSection($"workers:{description.Language}");

                    // if we're only loading 1 language, skip if the language is not equal to the target language
                    if (!string.IsNullOrEmpty(language) && !string.Equals(description.Language, language, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var arguments = new List<string>();
                    arguments.AddRange(workerConfig.Property("Arguments").Value.ToObject<string[]>());

                    var provider = new GenericWorkerProvider(description, arguments, workerDir);

                    providers.Add(provider);
                }
                catch (Exception e)
                {
                    logger.LogCritical(e, $"Failed to initialize worker provider for: {workerDir}");
                }
            }

            return providers;
        }
    }
}
