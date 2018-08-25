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
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class WorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private Dictionary<string, IWorkerProvider> _workerProviderDictionary = new Dictionary<string, IWorkerProvider>();

        public WorkerConfigFactory(IConfiguration config, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            WorkersDirPath = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var workersDirectorySection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.WorkersDirectorySectionName}");
            if (!string.IsNullOrEmpty(workersDirectorySection.Value))
            {
                WorkersDirPath = workersDirectorySection.Value;
            }
        }

        public List<IWorkerProvider> WorkerProviders => _workerProviderDictionary.Values.ToList();

        public string WorkersDirPath { get; }

        public IEnumerable<WorkerConfig> GetConfigs()
        {
            BuildWorkerProviderDictionary();
            foreach (var provider in WorkerProviders)
            {
                var description = provider.GetDescription();
                _logger.LogTrace($"Worker path for language worker {description.Language}: {description.WorkerDirectory}");

                var arguments = new WorkerProcessArguments()
                {
                    ExecutablePath = description.DefaultExecutablePath,
                    WorkerPath = description.GetWorkerPath()
                };

                if (description.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName))
                {
                    arguments.ExecutablePath = GetExecutablePathForJava(description.DefaultExecutablePath);
                }

                if (provider.TryConfigureArguments(arguments, _logger))
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

        internal void BuildWorkerProviderDictionary()
        {
            AddProviders();
            AddProvidersFromAppSettings();
        }

        internal void AddProviders()
        {
            var providers = new List<IWorkerProvider>();
            _logger.LogTrace($"Workers Directory set to: {WorkersDirPath}");

            foreach (var workerDir in Directory.EnumerateDirectories(WorkersDirPath))
            {
                string workerConfigPath = Path.Combine(workerDir, LanguageWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    AddProvider(workerDir);
                }
            }
        }

        internal void AddProvidersFromAppSettings()
        {
            var languagesSection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}");
            foreach (var languageSection in languagesSection.GetChildren())
            {
                var workerDirectorySection = languageSection.GetSection(LanguageWorkerConstants.WorkerDirectorySectionName);
                if (workerDirectorySection.Value != null)
                {
                    _workerProviderDictionary.Remove(languageSection.Key);
                    AddProvider(Path.Combine(workerDirectorySection.Value, languageSection.Key));
                }
            }
        }

        internal void AddProvider(string workerDir)
        {
            try
            {
                Dictionary<string, WorkerDescription> descriptionProfiles = new Dictionary<string, WorkerDescription>();
                string workerConfigPath = Path.Combine(workerDir, LanguageWorkerConstants.WorkerConfigFileName);
                if (!File.Exists(workerConfigPath))
                {
                    _logger.LogTrace($"Did not find worker config file at: {workerConfigPath}");
                    return;
                }
                _logger.LogTrace($"Found worker config: {workerConfigPath}");
                string json = File.ReadAllText(workerConfigPath);
                JObject workerConfig = JObject.Parse(json);
                WorkerDescription workerDescription = workerConfig.Property(LanguageWorkerConstants.WorkerDescription).Value.ToObject<WorkerDescription>();
                workerDescription.WorkerDirectory = workerDir;
                var languageSection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}");
                workerDescription.Arguments = workerDescription.Arguments ?? new List<string>();

                descriptionProfiles = GetWorkerDescriptionProfiles(workerConfig);
                if (ScriptSettingsManager.Instance.IsAppServiceEnvironment)
                {
                    //Overwrite default Description with AppServiceEnv profile
                    workerDescription = GetWorkerDescriptionFromProfiles(LanguageWorkerConstants.WorkerDescriptionAppServiceEnvProfileName, descriptionProfiles, workerDescription);
                }
                GetDefaultExecutablePathFromAppSettings(workerDescription, languageSection);
                AddArgumentsFromAppSettings(workerDescription, languageSection);
                if (File.Exists(workerDescription.GetWorkerPath()))
                {
                    _logger.LogTrace($"Will load worker provider for language: {workerDescription.Language}");
                    workerDescription.Validate();
                    _workerProviderDictionary[workerDescription.Language] = new GenericWorkerProvider(workerDescription, workerDir);
                }
                else
                {
                    throw new FileNotFoundException($"Did not find worker for for language: {workerDescription.Language}", workerDescription.GetWorkerPath());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to initialize worker provider for: {workerDir}");
            }
        }

        private static Dictionary<string, WorkerDescription> GetWorkerDescriptionProfiles(JObject workerConfig)
        {
            Dictionary<string, WorkerDescription> descriptionProfiles = new Dictionary<string, WorkerDescription>();
            var profiles = workerConfig.Property("profiles")?.Value.ToObject<JObject>();
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    string name = profile.Key;
                    JToken value = profile.Value;
                    WorkerDescription description = profile.Value.ToObject<WorkerDescription>();
                    descriptionProfiles.Add(name, description);
                }
            }
            return descriptionProfiles;
        }

        private static WorkerDescription GetWorkerDescriptionFromProfiles(string key, Dictionary<string, WorkerDescription> descriptionProfiles, WorkerDescription defaultWorkerDescription)
        {
            WorkerDescription profileDescription = null;
            if (descriptionProfiles.TryGetValue(key, out profileDescription))
            {
                profileDescription.Arguments = profileDescription.Arguments?.Count > 0 ? profileDescription.Arguments : defaultWorkerDescription.Arguments;
                profileDescription.DefaultExecutablePath = string.IsNullOrEmpty(profileDescription.DefaultExecutablePath) ? defaultWorkerDescription.DefaultExecutablePath : profileDescription.DefaultExecutablePath;
                profileDescription.DefaultWorkerPath = string.IsNullOrEmpty(profileDescription.DefaultWorkerPath) ? defaultWorkerDescription.DefaultWorkerPath : profileDescription.DefaultWorkerPath;
                profileDescription.Extensions = profileDescription.Extensions ?? defaultWorkerDescription.Extensions;
                profileDescription.Language = string.IsNullOrEmpty(profileDescription.Language) ? defaultWorkerDescription.Language : profileDescription.Language;
                profileDescription.WorkerDirectory = string.IsNullOrEmpty(profileDescription.WorkerDirectory) ? defaultWorkerDescription.WorkerDirectory : profileDescription.WorkerDirectory;
                return profileDescription;
            }
            return defaultWorkerDescription;
        }

        private static void GetDefaultExecutablePathFromAppSettings(WorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            var defaultExecutablePath = languageSection.GetSection($"{LanguageWorkerConstants.WorkerDescriptionDefaultExecutablePath}");
            workerDescription.DefaultExecutablePath = defaultExecutablePath.Value != null ? defaultExecutablePath.Value : workerDescription.DefaultExecutablePath;
        }

        private static void AddArgumentsFromAppSettings(WorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            var argumentsSection = languageSection.GetSection($"{LanguageWorkerConstants.WorkerDescriptionArguments}");
            if (argumentsSection.Value != null)
            {
                workerDescription.Arguments.AddRange(Regex.Split(argumentsSection.Value, @"\s+"));
            }
        }

        internal string GetExecutablePathForJava(string defaultExecutablePath)
        {
            string javaHome = ScriptSettingsManager.Instance.GetSetting("JAVA_HOME");
            if (string.IsNullOrEmpty(javaHome) || Path.IsPathRooted(defaultExecutablePath))
            {
                return defaultExecutablePath;
            }
            else
            {
                return Path.GetFullPath(Path.Combine(javaHome, "bin", defaultExecutablePath));
            }
        }
    }
}
