﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class WorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly IEnvironment _environment;
        private Dictionary<string, IWorkerProvider> _workerProviderDictionary = new Dictionary<string, IWorkerProvider>();

        public WorkerConfigFactory(IConfiguration config, ILogger logger, ISystemRuntimeInformation systemRuntimeInfo, IEnvironment environment)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            WorkersDirPath = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var workersDirectorySection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.WorkersDirectorySectionName}");
            if (!string.IsNullOrEmpty(workersDirectorySection.Value))
            {
                WorkersDirPath = workersDirectorySection.Value;
            }
        }

        public List<IWorkerProvider> WorkerProviders => _workerProviderDictionary.Values.ToList();

        public string WorkersDirPath { get; }

        public IList<WorkerConfig> GetConfigs()
        {
            BuildWorkerProviderDictionary();
            var result = new List<WorkerConfig>();

            foreach (var provider in WorkerProviders)
            {
                var description = provider.GetDescription();
                _logger.LogDebug($"Worker path for language worker {description.Language}: {description.WorkerDirectory}");

                string workerPath = description.GetWorkerPath();

                if (IsHydrationNeeded(workerPath))
                {
                    workerPath = GetHydratedWorkerPath(description);
                }

                var arguments = new WorkerProcessArguments()
                {
                    ExecutablePath = description.DefaultExecutablePath,
                    WorkerPath = workerPath
                };

                if (description.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName))
                {
                    arguments.ExecutablePath = GetExecutablePathForJava(description.DefaultExecutablePath);
                }

                if (provider.TryConfigureArguments(arguments, _logger))
                {
                    var config = new WorkerConfig()
                    {
                        Description = description,
                        Arguments = arguments
                    };
                    result.Add(config);
                }
                else
                {
                    _logger.LogError($"Could not configure language worker {description.Language}.");
                }
            }

            return result;
        }

        internal void BuildWorkerProviderDictionary()
        {
            AddProviders();
            AddProvidersFromAppSettings();
        }

        internal void AddProviders()
        {
            var providers = new List<IWorkerProvider>();
            _logger.LogDebug($"Workers Directory set to: {WorkersDirPath}");

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
                    AddProvider(workerDirectorySection.Value);
                }
            }
        }

        internal void AddProvider(string workerDir)
        {
            try
            {
                string workerConfigPath = Path.Combine(workerDir, LanguageWorkerConstants.WorkerConfigFileName);
                if (!File.Exists(workerConfigPath))
                {
                    _logger.LogDebug($"Did not find worker config file at: {workerConfigPath}");
                    return;
                }
                _logger.LogDebug($"Found worker config: {workerConfigPath}");
                string json = File.ReadAllText(workerConfigPath);
                JObject workerConfig = JObject.Parse(json);
                WorkerDescription workerDescription = workerConfig.Property(LanguageWorkerConstants.WorkerDescription).Value.ToObject<WorkerDescription>();
                workerDescription.WorkerDirectory = workerDir;
                var languageSection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}");
                workerDescription.Arguments = workerDescription.Arguments ?? new List<string>();

                GetDefaultExecutablePathFromAppSettings(workerDescription, languageSection);
                AddArgumentsFromAppSettings(workerDescription, languageSection);

                string workerPath = workerDescription.GetWorkerPath();

                if (IsHydrationNeeded(workerPath))
                {
                    workerPath = GetHydratedWorkerPath(workerDescription);
                }

                if (string.IsNullOrEmpty(workerPath) || File.Exists(workerPath))
                {
                    _logger.LogDebug($"Will load worker provider for language: {workerDescription.Language}");
                    workerDescription.Validate();
                    _workerProviderDictionary[workerDescription.Language] = new GenericWorkerProvider(workerDescription, workerDir);
                }
                else
                {
                    throw new FileNotFoundException($"Did not find worker for for language: {workerDescription.Language}", workerPath);
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

        internal static void AddArgumentsFromAppSettings(WorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            if (workerDescription.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName))
            {
                // For Java either provide arguments via JAVA_OPTS or languageWorkers:java:arguments. Both cannot be supported
                string javaOpts = ScriptSettingsManager.Instance.GetSetting("JAVA_OPTS");
                if (!string.IsNullOrEmpty(javaOpts))
                {
                    workerDescription.Arguments.Add(javaOpts);
                    return;
                }
            }
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

        internal bool IsHydrationNeeded(string workerPath)
        {
            if (string.IsNullOrEmpty(workerPath))
            {
                return false;
            }

            return workerPath.Contains(LanguageWorkerConstants.OSPlaceholder) ||
                    workerPath.Contains(LanguageWorkerConstants.ArchitecturePlaceholder) ||
                    workerPath.Contains(LanguageWorkerConstants.RuntimeVersionPlaceholder);
        }

        internal string GetHydratedWorkerPath(WorkerDescription description)
        {
            string workerPath = description.GetWorkerPath();
            if (string.IsNullOrEmpty(workerPath))
            {
                return null;
            }

            OSPlatform os = _systemRuntimeInformation.GetOSPlatform();

            Architecture architecture = _systemRuntimeInformation.GetOSArchitecture();
            string version = _environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeVersionSettingName);
            if (string.IsNullOrEmpty(version))
            {
                version = description.DefaultRuntimeVersion;
            }

            ValidateWorkerPath(description, workerPath, os, architecture, version);

            return workerPath.Replace(LanguageWorkerConstants.OSPlaceholder, os.ToString())
                             .Replace(LanguageWorkerConstants.ArchitecturePlaceholder, architecture.ToString())
                             .Replace(LanguageWorkerConstants.RuntimeVersionPlaceholder, version);
        }

        internal void ValidateWorkerPath(WorkerDescription description, string workerPath, OSPlatform os, Architecture architecture, string version)
        {
            string language = description.Language;
            if (workerPath.Contains(LanguageWorkerConstants.OSPlaceholder))
            {
                ValidateOSPlatform(description, os);
            }

            if (workerPath.Contains(LanguageWorkerConstants.ArchitecturePlaceholder))
            {
                ValidateArchitecture(description, architecture);
            }

            if (workerPath.Contains(LanguageWorkerConstants.RuntimeVersionPlaceholder) && !string.IsNullOrEmpty(version))
            {
                ValidateRuntimeVersion(description, version);
            }
        }

        internal void ValidateOSPlatform(WorkerDescription description, OSPlatform os)
        {
            string language = description.Language;
            if (!description.SupportedOperatingSystems.Any(s => s.Equals(os.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new PlatformNotSupportedException($"OS {os.ToString()} is not supported for language {language}");
            }
        }

        internal void ValidateArchitecture(WorkerDescription description, Architecture architecture)
        {
            string language = description.Language;
            if (!description.SupportedArchitectures.Any(s => s.Equals(architecture.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new PlatformNotSupportedException($"Architecture {architecture.ToString()} is not supported for language {language}");
            }
        }

        internal void ValidateRuntimeVersion(WorkerDescription description, string version)
        {
            string language = description.Language;
            if (!description.SupportedRuntimeVersions.Any(s => s.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"Version {version} is not supported for language {language}");
            }
        }
    }
}
