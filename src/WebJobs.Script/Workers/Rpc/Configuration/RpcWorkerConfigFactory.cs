// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class RpcWorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly string _workerRuntime;
        private readonly IEnvironment _environment;
        private readonly IWorkerConfigurationResolver _workerConfigurationResolver;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private Dictionary<string, RpcWorkerConfig> _workerDescriptionDictionary = new Dictionary<string, RpcWorkerConfig>();

        public RpcWorkerConfigFactory(IConfiguration config,
                                        ILogger logger,
                                        ISystemRuntimeInformation systemRuntimeInfo,
                                        IEnvironment environment,
                                        IMetricsLogger metricsLogger,
                                        IWorkerProfileManager workerProfileManager,
                                        IWorkerConfigurationResolver workerConfigurationResolver)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemRuntimeInformation = systemRuntimeInfo ?? throw new ArgumentNullException(nameof(systemRuntimeInfo));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            _workerConfigurationResolver = workerConfigurationResolver ?? throw new ArgumentNullException(nameof(workerConfigurationResolver));
        }

        public IList<RpcWorkerConfig> GetConfigs()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.GetConfigs))
            {
                BuildWorkerProviderDictionary();
                return _workerDescriptionDictionary.Values.ToList();
            }
        }

        internal void BuildWorkerProviderDictionary()
        {
            var workerConfigurationInfo = _workerConfigurationResolver.GetConfigurationInfo();

            AddProviders(workerConfigurationInfo);
            AddProvidersFromAppSettings(workerConfigurationInfo);
        }

        internal void AddProviders(WorkerConfigurationInfo workerConfigurationInfo)
        {
            var workerConfigs = workerConfigurationInfo.WorkerConfigPaths;

            foreach (var workerConfig in workerConfigs)
            {
                AddProvider(workerConfig, workerConfigurationInfo.WorkersRootDirPath);
            }
        }

        internal void AddProvidersFromAppSettings(WorkerConfigurationInfo workerConfigurationInfo)
        {
            var languagesSection = _config.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}");
            foreach (var languageSection in languagesSection.GetChildren())
            {
                var workerDirectorySection = languageSection.GetSection(WorkerConstants.WorkerDirectorySectionName);
                if (workerDirectorySection.Value != null)
                {
                    _workerDescriptionDictionary.Remove(languageSection.Key);
                    AddProvider(workerDirectorySection.Value, workerConfigurationInfo.WorkersRootDirPath);
                }
            }
        }

        internal void AddProvider(string workerDir, string workersRootDirPath)
        {
            using (_metricsLogger.LatencyEvent(string.Format(MetricEventNames.AddProvider, workerDir)))
            {
                try
                {
                    // After specialization, load worker config only for the specified runtime unless it's a multi-language app.
                    if (!string.IsNullOrWhiteSpace(_workerRuntime) && !_environment.IsPlaceholderModeEnabled() && !_environment.IsMultiLanguageRuntimeEnvironment())
                    {
                        string workerRuntime = Path.GetFileName(workerDir);
                        // Only skip worker directories that don't match the current runtime.
                        // Do not skip non-worker directories like the function app payload directory
                        if (!workerRuntime.Equals(_workerRuntime, StringComparison.OrdinalIgnoreCase) && workerDir.StartsWith(workersRootDirPath))
                        {
                            return;
                        }
                    }

                    string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);

                    if (!File.Exists(workerConfigPath))
                    {
                        _logger.LogDebug("Did not find worker config file at: {workerConfigPath}", workerConfigPath);
                        return;
                    }

                    _logger.LogDebug("Found worker config: {workerConfigPath}", workerConfigPath);

                    var workerConfig = GetWorkerConfigJsonElement(workerConfigPath);
                    var workerDescriptionElement = workerConfig.GetProperty(WorkerConstants.WorkerDescription);
                    var workerDescription = workerDescriptionElement.Deserialize<RpcWorkerDescription>(_jsonSerializerOptions);
                    workerDescription.WorkerDirectory = workerDir;

                    // Read the profiles from worker description and load the profile for which the conditions match
                    if (workerConfig.TryGetProperty(WorkerConstants.WorkerDescriptionProfiles, out var profiles))
                    {
                        List<WorkerDescriptionProfile> workerDescriptionProfiles = ReadWorkerDescriptionProfiles(profiles);
                        if (workerDescriptionProfiles.Count > 0)
                        {
                            _profileManager.SetWorkerDescriptionProfiles(workerDescriptionProfiles, workerDescription.Language);
                            _profileManager.LoadWorkerDescriptionFromProfiles(workerDescription, out workerDescription);
                        }
                    }

                    // Check if any app settings are provided for that language
                    var languageSection = _config.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}");
                    workerDescription.Arguments ??= new List<string>();
                    GetWorkerDescriptionFromAppSettings(workerDescription, languageSection);
                    AddArgumentsFromAppSettings(workerDescription, languageSection);

                    // Validate workerDescription
                    workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), _logger);

                    if (workerDescription.IsDisabled == true)
                    {
                        _logger.LogInformation("Skipping WorkerConfig for stack: {language} since it is disabled.", workerDescription.Language);
                        return;
                    }

                    if (ShouldAddWorkerConfig(workerDescription.Language))
                    {
                        workerDescription.FormatWorkerPathIfNeeded(_systemRuntimeInformation, _environment, _logger);
                        workerDescription.FormatWorkingDirectoryIfNeeded();
                        workerDescription.FormatArgumentsIfNeeded(_logger);
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

                        _logger.LogDebug("Added WorkerConfig for language: {language}", workerDescription.Language);
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    _logger.LogError(ex, "Failed to initialize worker provider for: {workerDir}", workerDir);
                }
            }
        }

        private static JsonElement GetWorkerConfigJsonElement(string workerConfigPath)
        {
            ReadOnlySpan<byte> jsonSpan = File.ReadAllBytes(workerConfigPath);

            if (jsonSpan.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
            {
                jsonSpan = jsonSpan[3..]; // Skip UTF-8 Byte Order Mark (BOM) if present at the beginning of the file.
            }

            var reader = new Utf8JsonReader(jsonSpan, isFinalBlock: true, state: default);
            using var doc = JsonDocument.ParseValue(ref reader);

            return doc.RootElement.Clone();
        }

        private List<WorkerDescriptionProfile> ReadWorkerDescriptionProfiles(JsonElement profilesElement)
        {
            var profiles = profilesElement.Deserialize<IList<WorkerProfileDescriptor>>(_jsonSerializerOptions);

            if (profiles == null || profiles.Count <= 0)
            {
                return new List<WorkerDescriptionProfile>(0);
            }

            var descriptionProfiles = new List<WorkerDescriptionProfile>(profiles.Count);

            try
            {
                foreach (var profile in profiles)
                {
                    var profileConditions = new List<IWorkerProfileCondition>(profile.Conditions.Count);

                    foreach (var descriptor in profile.Conditions)
                    {
                        if (!_profileManager.TryCreateWorkerProfileCondition(descriptor, out IWorkerProfileCondition condition))
                        {
                            // Failed to resolve condition. This profile will be disabled using a mock false condition
                            _logger.LogInformation("Profile {name} is disabled. Cannot resolve the profile condition {condition}", profile.ProfileName, descriptor.Type);
                            condition = new FalseCondition();
                        }

                        profileConditions.Add(condition);
                    }

                    descriptionProfiles.Add(new(profile.ProfileName, profileConditions, profile.Description));
                }
            }
            catch (Exception)
            {
                throw new FormatException("Failed to parse profiles in worker config.");
            }

            return descriptionProfiles;
        }

        internal WorkerProcessCountOptions GetWorkerProcessCount(JsonElement workerConfig)
        {
            WorkerProcessCountOptions workerProcessCount = null;

            if (workerConfig.TryGetProperty(WorkerConstants.ProcessCount, out var processCountElement))
            {
                workerProcessCount = processCountElement.Deserialize<WorkerProcessCountOptions>(_jsonSerializerOptions);
            }

            workerProcessCount ??= new WorkerProcessCountOptions();

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
                throw new ArgumentOutOfRangeException(nameof(workerProcessCount.ProcessCount), "ProcessCount must be greater than 0.");
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

            if (_environment.IsMultiLanguageRuntimeEnvironment())
            {
                _logger.LogInformation("Found multi-language runtime environment. Starting WorkerConfig for language: {workerDescriptionLanguage}", workerDescriptionLanguage);
                return true;
            }

            if (!string.IsNullOrEmpty(_workerRuntime))
            {
                _logger.LogDebug("EnvironmentVariable {functionWorkerRuntimeSettingName}: {workerRuntime}", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime);
                if (_workerRuntime.Equals(workerDescriptionLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // After specialization only create worker provider for the language set by FUNCTIONS_WORKER_RUNTIME env variable
                _logger.LogInformation("{FUNCTIONS_WORKER_RUNTIME} set to {workerRuntime}. Skipping WorkerConfig for language: {workerDescriptionLanguage}", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, _workerRuntime, workerDescriptionLanguage);
                return false;
            }

            return true;
        }

        private void ReadLanguageWorkerFile(string workerPath)
        {
            if (!_environment.IsPlaceholderModeEnabled()
                || string.IsNullOrWhiteSpace(_workerRuntime)
                || !File.Exists(workerPath))
            {
                return;
            }

            // Reads the file to warm up the operating system's file cache. Can run in the background.
            _ = Task.Run(() =>
            {
                const int bufferSize = 4096;
                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                try
                {
                    using var fs = new FileStream(
                        workerPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize,
                        FileOptions.SequentialScan);

                    while (fs.Read(buffer, 0, bufferSize) > 0)
                    {
                        // Do nothing. The goal is to read the file into the OS cache.
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error warming up worker file: {filePath}", workerPath);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            });
        }
    }
}
