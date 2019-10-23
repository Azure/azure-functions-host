// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
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
        private readonly IMetricsLogger _metricsLogger;
        private readonly IEnvironment _environment;

        private Dictionary<string, RpcWorkerDescription> _workerDescripionDictionary = new Dictionary<string, RpcWorkerDescription>();

        public WorkerConfigFactory(IConfiguration config, ILogger logger, ISystemRuntimeInformation systemRuntimeInfo, IEnvironment environment, IMetricsLogger metricsLogger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger;
            WorkersDirPath = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var workersDirectorySection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{OutOfProcConstants.WorkersDirectorySectionName}");
            if (!string.IsNullOrEmpty(workersDirectorySection.Value))
            {
                WorkersDirPath = workersDirectorySection.Value;
            }
        }

        public string WorkersDirPath { get; }

        public IList<WorkerConfig> GetConfigs()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.GetConfigs))
            {
                BuildWorkerProviderDictionary();
                var result = new List<WorkerConfig>();

                foreach (var description in _workerDescripionDictionary.Values)
                {
                    _logger.LogDebug($"Worker path for language worker {description.Language}: {description.WorkerDirectory}");

                    if (IsHydrationNeeded(description.DefaultWorkerPath))
                    {
                        description.DefaultWorkerPath = GetHydratedWorkerPath(description);
                    }

                    var arguments = new WorkerProcessArguments()
                    {
                        ExecutablePath = description.DefaultExecutablePath,
                        WorkerPath = description.DefaultWorkerPath
                    };

                    if (description.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName))
                    {
                        arguments.ExecutablePath = GetExecutablePathForJava(description.DefaultExecutablePath);
                    }
                    arguments.ExecutableArguments.AddRange(description.Arguments);
                    var config = new WorkerConfig()
                    {
                        Description = description,
                        Arguments = arguments
                    };
                    result.Add(config);
                }

                return result;
            }
        }

        internal void BuildWorkerProviderDictionary()
        {
            AddProviders();
            AddProvidersFromAppSettings();
        }

        internal void AddProviders()
        {
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
                var workerDirectorySection = languageSection.GetSection(OutOfProcConstants.WorkerDirectorySectionName);
                if (workerDirectorySection.Value != null)
                {
                    _workerDescripionDictionary.Remove(languageSection.Key);
                    AddProvider(workerDirectorySection.Value);
                }
            }
        }

        internal void AddProvider(string workerDir)
        {
            using (_metricsLogger.LatencyEvent(string.Format(MetricEventNames.AddProvider, workerDir)))
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
                    RpcWorkerDescription workerDescription = workerConfig.Property(OutOfProcConstants.WorkerDescription).Value.ToObject<RpcWorkerDescription>();
                    workerDescription.WorkerDirectory = workerDir;
                    var languageSection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}");
                    workerDescription.Arguments = workerDescription.Arguments ?? new List<string>();
                    GetDefaultExecutablePathFromAppSettings(workerDescription, languageSection);
                    AddArgumentsFromAppSettings(workerDescription, languageSection);
                    if (IsHydrationNeeded(workerDescription.DefaultWorkerPath))
                    {
                        workerDescription.DefaultWorkerPath = GetHydratedWorkerPath(workerDescription);
                    }
                    workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory());
                    _workerDescripionDictionary[workerDescription.Language] = workerDescription;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to initialize worker provider for: {workerDir}");
                }
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

        private static WorkerDescription GetWorkerDescriptionFromProfiles(string key, Dictionary<string, RpcWorkerDescription> descriptionProfiles, RpcWorkerDescription defaultWorkerDescription)
        {
            RpcWorkerDescription profileDescription = null;
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
            var defaultExecutablePath = languageSection.GetSection($"{OutOfProcConstants.WorkerDescriptionDefaultExecutablePath}");
            workerDescription.DefaultExecutablePath = defaultExecutablePath.Value != null ? defaultExecutablePath.Value : workerDescription.DefaultExecutablePath;
        }

        internal static void AddArgumentsFromAppSettings(RpcWorkerDescription workerDescription, IConfigurationSection languageSection)
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
            var argumentsSection = languageSection.GetSection($"{OutOfProcConstants.WorkerDescriptionArguments}");
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

        internal string GetHydratedWorkerPath(RpcWorkerDescription description)
        {
            if (string.IsNullOrEmpty(description.DefaultWorkerPath))
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

            description.ValidateWorkerPath(description.DefaultWorkerPath, os, architecture, version);

            return description.DefaultWorkerPath.Replace(LanguageWorkerConstants.OSPlaceholder, os.ToString())
                             .Replace(LanguageWorkerConstants.ArchitecturePlaceholder, architecture.ToString())
                             .Replace(LanguageWorkerConstants.RuntimeVersionPlaceholder, version);
        }
    }
}