// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        private readonly string _workerRuntime;
        private readonly IEnvironment _environment;

        private Dictionary<string, RpcWorkerConfig> _workerDescriptionDictionary = new Dictionary<string, RpcWorkerConfig>();

        public RpcWorkerConfigFactory(IConfiguration config, ILogger logger, ISystemRuntimeInformation systemRuntimeInfo, IEnvironment environment, IMetricsLogger metricsLogger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger;
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
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
                return _workerDescriptionDictionary.Values.ToList();
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
                    _workerDescriptionDictionary.Remove(languageSection.Key);
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
                        workerDescription.ThrowIfFileNotExists(workerDescription.DefaultWorkerPath, nameof(workerDescription.DefaultWorkerPath));
                        workerDescription.ExpandEnvironmentVariables();

                        WorkerProcessCountOptions workerProcessCount = GetWorkerProcessCount(workerConfig);

                        var arguments = new WorkerProcessArguments()
                        {
                            ExecutablePath = workerDescription.DefaultExecutablePath,
                            WorkerPath = workerDescription.DefaultWorkerPath
                        };
                        arguments.ExecutableArguments.AddRange(workerDescription.Arguments);
                        var rpcWorkerConfig = new RpcWorkerConfig()
                        {
                            Description = workerDescription,
                            Arguments = arguments,
                            CountOptions = workerProcessCount,
                        };
                        _workerDescriptionDictionary[workerDescription.Language] = rpcWorkerConfig;
                        ReadLanguageWorkerFile(arguments.WorkerPath);
                        _logger.LogDebug($"Added WorkerConfig for language: {workerDescription.Language}");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to initialize worker provider for: {workerDir}");
                }
            }
        }

        internal WorkerProcessCountOptions GetWorkerProcessCount(JObject workerConfig)
        {
            WorkerProcessCountOptions workerProcessCount = workerConfig.Property(WorkerConstants.ProcessCount)?.Value.ToObject<WorkerProcessCountOptions>();

            workerProcessCount = workerProcessCount ?? new WorkerProcessCountOptions();

            if (workerProcessCount.SetProcessCountToNumberOfCpuCores)
            {
                workerProcessCount.ProcessCount = _environment.GetEffectiveCoresCount();
                // set Max worker process count to Number of effective cores if MaxProcessCount is less than MinProcessCount
                workerProcessCount.MaxProcessCount = workerProcessCount.ProcessCount > workerProcessCount.MaxProcessCount ? workerProcessCount.ProcessCount : workerProcessCount.MaxProcessCount;
            }

            // Env variable takes precedence over worker.config
            string processCountEnvSetting = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName);
            if (!string.IsNullOrEmpty(processCountEnvSetting))
            {
                workerProcessCount.ProcessCount = int.Parse(processCountEnvSetting) > 1 ? int.Parse(processCountEnvSetting) : 1;
            }

            // Validate
            if (workerProcessCount.ProcessCount <= 0)
            {
                throw new ArgumentOutOfRangeException($"{nameof(workerProcessCount.ProcessCount)}",  "ProcessCount must be greater than 0.");
            }
            if (workerProcessCount.ProcessCount > workerProcessCount.MaxProcessCount)
            {
                throw new ArgumentException($"{nameof(workerProcessCount.ProcessCount)} must not be greater than {nameof(workerProcessCount.MaxProcessCount)}");
            }
            if (workerProcessCount.ProcessStartupInterval.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException($"{nameof(workerProcessCount.ProcessStartupInterval)}", "The TimeSpan must not be negative.");
            }

            return workerProcessCount;
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
            var argumentsSection = languageSection.GetSection($"{WorkerConstants.WorkerDescriptionArguments}");
            if (argumentsSection.Value != null)
            {
                ((List<string>)workerDescription.Arguments).AddRange(Regex.Split(argumentsSection.Value, @"\s+"));
            }
        }

        internal bool ShouldAddWorkerConfig(string workerDescriptionLanguage)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return true;
            }

            if (!string.IsNullOrEmpty(_workerRuntime))
            {
                _logger.LogDebug($"EnvironmentVariable {RpcWorkerConstants.FunctionWorkerRuntimeSettingName}: {_workerRuntime}");
                if (_workerRuntime.Equals(workerDescriptionLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                // After specialization only create worker provider for the language set by FUNCTIONS_WORKER_RUNTIME env variable
                _logger.LogInformation($"{RpcWorkerConstants.FunctionWorkerRuntimeSettingName} set to {_workerRuntime}. Skipping WorkerConfig for language:{workerDescriptionLanguage}");
                return false;
            }
            return true;
        }

        internal void ReadLanguageWorkerFile(string workerPath)
        {
            if (_environment.IsPlaceholderModeEnabled() &&
                 !string.IsNullOrEmpty(_workerRuntime) &&
                 File.Exists(workerPath))
            {
                // Read lanaguage worker file to avoid disk reads during specialization. This is only to page-in bytes.
                File.ReadAllBytes(workerPath);
            }
        }
    }
}