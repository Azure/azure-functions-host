// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class RpcWorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IEnvironment _environment;

        private Dictionary<string, RpcWorkerDescription> _workerDescripionDictionary = new Dictionary<string, RpcWorkerDescription>();

        public RpcWorkerConfigFactory(IConfiguration config, ILogger logger, ISystemRuntimeInformation systemRuntimeInfo, IEnvironment environment, IMetricsLogger metricsLogger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger;
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath);
            WorkersDirPath = GetDefaultWorkersDirectory(Directory.Exists);
            var workersDirectorySection = _config.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}");
            if (!string.IsNullOrEmpty(workersDirectorySection.Value))
            {
                WorkersDirPath = workersDirectorySection.Value;
            }
        }

        public string WorkersDirPath { get; }

        public IList<RpcWorkerConfig> GetConfigs()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.GetConfigs))
            {
                BuildWorkerProviderDictionary();
                var result = new List<RpcWorkerConfig>();

                foreach (var description in _workerDescripionDictionary.Values)
                {
                    _logger.LogDebug($"Worker path for language worker {description.Language}: {description.WorkerDirectory}");

                    var arguments = new WorkerProcessArguments()
                    {
                        ExecutablePath = description.DefaultExecutablePath,
                        WorkerPath = description.DefaultWorkerPath
                    };

                    if (description.Language.Equals(RpcWorkerConstants.JavaLanguageWorkerName))
                    {
                        arguments.ExecutablePath = GetExecutablePathForJava(description.DefaultExecutablePath);
                    }
                    arguments.ExecutableArguments.AddRange(description.Arguments);
                    var config = new RpcWorkerConfig()
                    {
                        Description = description,
                        Arguments = arguments
                    };
                    result.Add(config);
                }

                return result;
            }
        }

        internal static string GetDefaultWorkersDirectory(Func<string, bool> directoryExists)
        {
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath);
            string workersDirPath = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            if (!directoryExists(workersDirPath))
            {
                // Site Extension. Default to parent directory
                workersDirPath = Path.Combine(Directory.GetParent(assemblyLocalPath).FullName, RpcWorkerConstants.DefaultWorkersDirectoryName);
            }
            return workersDirPath;
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
                string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    AddProvider(workerDir);
                }
            }
        }

        internal void AddProvidersFromAppSettings()
        {
            var languagesSection = _config.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}");
            foreach (var languageSection in languagesSection.GetChildren())
            {
                var workerDirectorySection = languageSection.GetSection(WorkerConstants.WorkerDirectorySectionName);
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
                    string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
                    if (!File.Exists(workerConfigPath))
                    {
                        _logger.LogDebug($"Did not find worker config file at: {workerConfigPath}");
                        return;
                    }
                    // Parse worker config file
                    _logger.LogDebug($"Found worker config: {workerConfigPath}");
                    string json = File.ReadAllText(workerConfigPath);
                    JObject workerConfig = JObject.Parse(json);
                    RpcWorkerDescription workerDescription = workerConfig.Property(WorkerConstants.WorkerDescription).Value.ToObject<RpcWorkerDescription>();
                    workerDescription.WorkerDirectory = workerDir;

                    // Check if any appsettings are provided for that langauge
                    var languageSection = _config.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}");
                    workerDescription.Arguments = workerDescription.Arguments ?? new List<string>();
                    GetWorkerDescriptionFromAppSettings(workerDescription, languageSection);
                    AddArgumentsFromAppSettings(workerDescription, languageSection);

                    // Validate workerDescription
                    workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), _logger);

                    if (ShouldAddWorkerConfig(workerDescription.Language))
                    {
                        workerDescription.FormatWorkerPathIfNeeded(_systemRuntimeInformation, _environment, _logger);
                        workerDescription.ThrowIfDefaultWorkerPathNotExists();
                        _workerDescripionDictionary[workerDescription.Language] = workerDescription;
                        _logger.LogDebug($"Added WorkerConfig for language: {workerDescription.Language}");
                    }
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

        private static void GetWorkerDescriptionFromAppSettings(RpcWorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            var defaultExecutablePathSetting = languageSection.GetSection($"{WorkerConstants.WorkerDescriptionDefaultExecutablePath}");
            workerDescription.DefaultExecutablePath = defaultExecutablePathSetting.Value != null ? defaultExecutablePathSetting.Value : workerDescription.DefaultExecutablePath;

            var defaultRuntimeVersionAppSetting = languageSection.GetSection($"{WorkerConstants.WorkerDescriptionDefaultRuntimeVersion}");
            workerDescription.DefaultRuntimeVersion = defaultRuntimeVersionAppSetting.Value != null ? defaultRuntimeVersionAppSetting.Value : workerDescription.DefaultRuntimeVersion;
        }

        internal static void AddArgumentsFromAppSettings(RpcWorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            if (workerDescription.Language.Equals(RpcWorkerConstants.JavaLanguageWorkerName))
            {
                // For Java either provide arguments via JAVA_OPTS or languageWorkers:java:arguments. Both cannot be supported
                string javaOpts = ScriptSettingsManager.Instance.GetSetting("JAVA_OPTS");
                if (!string.IsNullOrEmpty(javaOpts))
                {
                    workerDescription.Arguments.Add(javaOpts);
                    return;
                }
            }
            var argumentsSection = languageSection.GetSection($"{WorkerConstants.WorkerDescriptionArguments}");
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

        internal bool ShouldAddWorkerConfig(string workerDescriptionLanguage)
        {
            string workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (_environment.IsPlaceholderModeEnabled())
            {
                return true;
            }

            if (!string.IsNullOrEmpty(workerRuntime))
            {
                _logger.LogDebug($"EnvironmentVariable {RpcWorkerConstants.FunctionWorkerRuntimeSettingName}: {workerRuntime}");
                if (workerRuntime.Equals(workerDescriptionLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                // After specialization only create worker provider for the language set by FUNCTIONS_WORKER_RUNTIME env variable
                _logger.LogInformation($"{RpcWorkerConstants.FunctionWorkerRuntimeSettingName} set to {workerRuntime}. Skipping WorkerConfig for language:{workerDescriptionLanguage}");
                return false;
            }
            return true;
        }
    }
}