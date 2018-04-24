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
            return _workerDescription;
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
            var workerDirPath = settingsManager.Configuration.GetSection("workers:config:path").Value ?? WorkerProviderHelper.GetDefaultWorkerDirectoryPath();

            if (!string.IsNullOrEmpty(language))
            {
                logger.LogInformation($"Reading Worker config for the language: {language}");
                string languageWorkerDirectory = Path.Combine(workerDirPath, language);
                var provider = GetProviderFromConfig(languageWorkerDirectory, logger);
                if (provider != null)
                {
                    providers.Add(provider);
                    logger.LogTrace($"Successfully added WorkerProvider for: {language}");
                }
            }
            else
            {
                logger.LogInformation($"Loading all the worker providers from the default workers directory: {workerDirPath}");
                foreach (var workerDir in Directory.EnumerateDirectories(workerDirPath))
                {
                    var provider = GetProviderFromConfig(workerDir, logger);
                    if (provider != null)
                    {
                        providers.Add(provider);
                    }
                }
            }
            return providers;
        }

        public static IWorkerProvider GetProviderFromConfig(string workerDir, ILogger logger)
        {
            try
            {
                string workerConfigPath = Path.Combine(workerDir, ScriptConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    logger.LogInformation($"Found worker config: {workerConfigPath}");
                    string json = File.ReadAllText(workerConfigPath);
                    JObject workerConfig = JObject.Parse(json);
                    WorkerDescription workerDescription = workerConfig.Property(WorkerConstants.Description).Value.ToObject<WorkerDescription>();
                    var arguments = new List<string>();
                    arguments.AddRange(workerConfig.Property(WorkerConstants.Arguments).Value.ToObject<string[]>());
                    logger.LogInformation($"Will load worker provider for language: {workerDescription.Language}");
                    return new GenericWorkerProvider(workerDescription, arguments, workerDir);
                }
                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Failed to initialize worker provider for: {workerDir}");
                return null;
            }
        }
    }
}
