// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        private string _pathToWorkerDir;
        private static List<string> _languagesFromAppSettings = new List<string>();

        public GenericWorkerProvider(WorkerDescription workerDescription, string pathToWorkerDir)
        {
            _workerDescription = workerDescription ?? throw new ArgumentNullException(nameof(workerDescription));
            _pathToWorkerDir = pathToWorkerDir ?? throw new ArgumentNullException(nameof(pathToWorkerDir));
        }

        public WorkerDescription GetDescription()
        {
            return _workerDescription;
        }

        public bool TryConfigureArguments(WorkerProcessArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            if (_workerDescription.Arguments != null)
            {
                args.ExecutableArguments.AddRange(_workerDescription.Arguments);
            }
            return true;
        }

        public string GetWorkerDirectoryPath()
        {
            return _pathToWorkerDir;
        }

        public static List<IWorkerProvider> ReadWorkerProviderFromConfig(ScriptHostConfiguration config, string workersDirPath, ILogger logger, ScriptSettingsManager settingsManager = null, string language = null)
        {
            var providers = new List<IWorkerProvider>();
            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            logger.LogTrace($"Workers Directory set to: {workersDirPath}");

            if (!string.IsNullOrEmpty(language))
            {
                logger.LogInformation($"Reading Worker config for the language: {language}");
                string languageWorkerDirectory = Path.Combine(workersDirPath, language);
                var provider = GetProviderFromConfig(languageWorkerDirectory, logger, settingsManager);
                if (provider != null)
                {
                    providers.Add(provider);
                    logger.LogTrace($"Added WorkerProvider for: {language}");
                }
            }
            else
            {
                logger.LogTrace($"Loading worker providers from the workers directory: {workersDirPath}");
                foreach (var workerDir in Directory.EnumerateDirectories(workersDirPath))
                {
                    var provider = GetProviderFromConfig(workerDir, logger, settingsManager);
                    if (provider != null)
                    {
                        providers.Add(provider);
                    }
                }
            }
            return providers;
        }

        public static IWorkerProvider GetProviderFromConfig(string workerDir, ILogger logger, ScriptSettingsManager settingsManager)
        {
            try
            {
                string workerConfigPath = Path.Combine(workerDir, LanguageWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    logger.LogTrace($"Found worker config: {workerConfigPath}");
                    string json = File.ReadAllText(workerConfigPath);
                    JObject workerConfig = JObject.Parse(json);
                    WorkerDescription workerDescription = workerConfig.Property(LanguageWorkerConstants.WorkerDescription).Value.ToObject<WorkerDescription>();

                    var languageSection = settingsManager.Configuration.GetSection($"{LanguageWorkerConstants.LanguageWorkerSectionName}:{workerDescription.Language}");
                    workerDescription.Arguments = workerDescription.Arguments ?? new List<string>();
                    var argumentsSection = languageSection.GetSection($"{LanguageWorkerConstants.WorkerDescriptionArguments}");
                    if (argumentsSection.Value != null)
                    {
                       workerDescription.Arguments.AddRange(Regex.Split(argumentsSection.Value, @"\s+"));
                    }
                    logger.LogTrace($"Will load worker provider for language: {workerDescription.Language}");
                    return new GenericWorkerProvider(workerDescription, workerDir);
                }
                logger.LogTrace($"Did not find worker config file at: {workerConfigPath}");
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
