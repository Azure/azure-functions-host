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
        private WorkerDescription workerDescription;
        private List<string> arguments;

        public GenericWorkerProvider(WorkerDescription workerDescription, List<string> arguments)
        {
            this.workerDescription = workerDescription ?? throw new ArgumentNullException(nameof(workerDescription));
            this.arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        }

        public WorkerDescription GetDescription()
        {
            return this.workerDescription;
        }

        public bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            args.ExecutableArguments.AddRange(arguments);
            return true;
        }

        public static List<IWorkerProvider> ReadWorkerProviderFromConfig(ScriptHostConfiguration config, ILogger logger, ScriptSettingsManager settingsManager = null)
        {
            var providers = new List<IWorkerProvider>();
            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            var assemblyDir = Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath);

            var workerDirPath = settingsManager.Configuration.GetSection("workers:config:path").Value ?? Path.Combine(assemblyDir, "workers");

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

                    var arguments = new List<string>();
                    arguments.AddRange(workerConfig.Property("Arguments").Value.ToObject<string[]>());

                    var provider = new GenericWorkerProvider(description, arguments);

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
