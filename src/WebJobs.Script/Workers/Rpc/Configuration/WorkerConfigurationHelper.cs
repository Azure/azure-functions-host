// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal static class WorkerConfigurationHelper
    {
        internal static RpcWorkerConfig AddProvider(WorkerConfigurationResolverOptions resolverOptions,
                                                    string workerDir,
                                                    IMetricsLogger metricsLogger,
                                                    ILogger logger,
                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                    IWorkerProfileManager profileManager)
        {
            using (metricsLogger.LatencyEvent(string.Format(MetricEventNames.AddProvider, workerDir)))
            {
                try
                {
                    string workerRuntime = resolverOptions.WorkerRuntime;
                    // After specialization, load worker config only for the specified runtime unless it's a multi-language app.
                    if (!string.IsNullOrWhiteSpace(resolverOptions.WorkerRuntime) && !resolverOptions.IsPlaceholderModeEnabled && !resolverOptions.IsMultiLanguageWorkerEnvironment)
                    {
                        string workerName = Path.GetFileName(workerDir);
                        // Only skip worker directories that don't match the current runtime.
                        // Do not skip non-worker directories like the function app payload directory
                        if (!workerName.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase) && workerDir.StartsWith(resolverOptions.WorkersRootDirPath))
                        {
                            return null;
                        }
                    }

                    string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);

                    if (!File.Exists(workerConfigPath))
                    {
                        logger.LogDebug("Did not find worker config file at: {workerConfigPath}", workerConfigPath);
                        return null;
                    }

                    logger.LogDebug("Found worker config: {workerConfigPath}", workerConfigPath);

                    var workerConfig = GetWorkerConfigJsonElement(workerConfigPath);

                    RpcWorkerDescription workerDescription = GetWorkerDescription(workerConfig, workerDir, profileManager, resolverOptions.LanguageWorkersSettings, logger);

                    if (workerDescription.IsDisabled == true)
                    {
                        logger.LogInformation("Skipping WorkerConfig for stack: {language} since it is disabled.", workerDescription.Language);
                        return null;
                    }

                    if (ShouldAddWorkerConfig(workerDescription.Language, resolverOptions.IsPlaceholderModeEnabled, resolverOptions.IsMultiLanguageWorkerEnvironment, logger, workerRuntime))
                    {
                        workerDescription.FormatWorkerPathIfNeeded(systemRuntimeInformation, workerRuntime, resolverOptions.FunctionWorkerRuntimeVersion, resolverOptions.EffectiveCoresCount, logger);
                        workerDescription.FormatWorkingDirectoryIfNeeded();
                        workerDescription.FormatArgumentsIfNeeded(logger);
                        workerDescription.ThrowIfFileNotExists(workerDescription.DefaultWorkerPath, nameof(workerDescription.DefaultWorkerPath));
                        workerDescription.ExpandEnvironmentVariables();

                        WorkerProcessCountOptions workerProcessCount = GetWorkerProcessCount(workerConfig, resolverOptions.FunctionsWorkerProcessCount, resolverOptions.EffectiveCoresCount);

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

                        ReadLanguageWorkerFile(arguments.WorkerPath, resolverOptions.IsPlaceholderModeEnabled, logger, workerRuntime);

                        logger.LogDebug("Added WorkerConfig for language: {language} with worker path: {path}", workerDescription.Language, workerDescription.DefaultWorkerPath);

                        return rpcWorkerConfig;
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    logger.LogError(ex, "Failed to initialize worker provider for: {workerDir}", workerDir);
                }
            }

            return null;
        }

        internal static WorkerProcessCountOptions GetWorkerProcessCount(JsonElement workerConfig, string functionsWorkerProcessCountSettingName, int coreCount)
        {
            WorkerProcessCountOptions workerProcessCount = null;
            var jsonSerializerOptions = JsonSerializerOptionsProvider.WorkerConfigJsonSerializerOptions;

            if (workerConfig.TryGetProperty(WorkerConstants.ProcessCount, out var processCountElement))
            {
                workerProcessCount = processCountElement.Deserialize<WorkerProcessCountOptions>(jsonSerializerOptions);
            }

            workerProcessCount ??= new WorkerProcessCountOptions();

            if (workerProcessCount.SetProcessCountToNumberOfCpuCores)
            {
                workerProcessCount.ProcessCount = coreCount;
                // set Max worker process count to Number of effective cores if MaxProcessCount is less than MinProcessCount
                workerProcessCount.MaxProcessCount = workerProcessCount.ProcessCount > workerProcessCount.MaxProcessCount ? workerProcessCount.ProcessCount : workerProcessCount.MaxProcessCount;
            }

            // Env variable takes precedence over worker.config
            string processCountEnvSetting = functionsWorkerProcessCountSettingName;
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

        internal static bool ShouldAddWorkerConfig(string workerDescriptionLanguage, bool placeholderMode, bool multiLanguageWorkerEnvironment, ILogger logger, string workerRuntime)
        {
            if (placeholderMode)
            {
                return true;
            }

            if (multiLanguageWorkerEnvironment)
            {
                logger.LogInformation("Found multi-language runtime environment. Starting WorkerConfig for language: {workerDescriptionLanguage}", workerDescriptionLanguage);
                return true;
            }

            if (!string.IsNullOrEmpty(workerRuntime))
            {
                logger.LogDebug("EnvironmentVariable {functionWorkerRuntimeSettingName}: {workerRuntime}", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
                if (workerRuntime.Equals(workerDescriptionLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // After specialization only create worker provider for the language set by FUNCTIONS_WORKER_RUNTIME env variable
                logger.LogInformation("{FUNCTIONS_WORKER_RUNTIME} set to {workerRuntime}. Skipping WorkerConfig for language: {workerDescriptionLanguage}", RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime, workerDescriptionLanguage);
                return false;
            }

            return true;
        }

        private static void ReadLanguageWorkerFile(string workerPath, bool placeHolderMode, ILogger logger, string workerRuntime)
        {
            if (!placeHolderMode
                || string.IsNullOrWhiteSpace(workerRuntime)
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
                    logger.LogError(ex, "Unexpected error warming up worker file: {filePath}", workerPath);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            });
        }

        internal static RpcWorkerDescription GetWorkerDescription(
            JsonElement workerConfig,
            string workerDir,
            IWorkerProfileManager profileManager,
            ImmutableDictionary<string, string> languageWorkersSettings,
            ILogger logger)
        {
            var jsonSerializerOptions = JsonSerializerOptionsProvider.WorkerConfigJsonSerializerOptions;
            var workerDescriptionElement = workerConfig.GetProperty(WorkerConstants.WorkerDescription);
            var workerDescription = workerDescriptionElement.Deserialize<RpcWorkerDescription>(jsonSerializerOptions);
            workerDescription.WorkerDirectory = workerDir;

            // Read the profiles from worker description and load the profile for which the conditions match
            if (workerConfig.TryGetProperty(WorkerConstants.WorkerDescriptionProfiles, out var profiles))
            {
                List<WorkerDescriptionProfile> workerDescriptionProfiles = ReadWorkerDescriptionProfiles(profiles, jsonSerializerOptions, profileManager, logger);
                if (workerDescriptionProfiles.Count > 0)
                {
                    profileManager.SetWorkerDescriptionProfiles(workerDescriptionProfiles, workerDescription.Language);
                    profileManager.LoadWorkerDescriptionFromProfiles(workerDescription, out workerDescription);
                }
            }

            workerDescription.Arguments ??= new List<string>();

            if (languageWorkersSettings is not null)
            {
                // Check if any app settings are provided for that language
                GetWorkerDescriptionFromAppSettings(workerDescription, languageWorkersSettings);
                AddArgumentsFromAppSettings(workerDescription, languageWorkersSettings);
            }

            // Validate workerDescription
            workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), logger);

            return workerDescription;
        }

        internal static JsonElement GetWorkerConfigJsonElement(string workerConfigPath)
        {
            ReadOnlySpan<byte> jsonSpan = File.ReadAllBytes(workerConfigPath);

            if (jsonSpan.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
            {
                jsonSpan = jsonSpan[3..]; // Skip UTF-8 Byte Order Mark (BOM) if present at the beginning of the file.
            }

            if (jsonSpan.IsEmpty)
            {
                return default; // Return default JsonElement if the file is empty.
            }

            var reader = new Utf8JsonReader(jsonSpan, isFinalBlock: true, state: default);
            using var doc = JsonDocument.ParseValue(ref reader);

            return doc.RootElement.Clone();
        }

        private static List<WorkerDescriptionProfile> ReadWorkerDescriptionProfiles(JsonElement profilesElement,
                                                                            JsonSerializerOptions jsonSerializerOptions,
                                                                            IWorkerProfileManager profileManager,
                                                                            ILogger logger)
        {
            var profiles = profilesElement.Deserialize<IList<WorkerProfileDescriptor>>(jsonSerializerOptions);

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
                        if (!profileManager.TryCreateWorkerProfileCondition(descriptor, out IWorkerProfileCondition condition))
                        {
                            // Failed to resolve condition. This profile will be disabled using a mock false condition
                            logger.LogInformation("Profile {name} is disabled. Cannot resolve the profile condition {condition}", profile.ProfileName, descriptor.Type);
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

        private static void GetWorkerDescriptionFromAppSettings(RpcWorkerDescription workerDescription, ImmutableDictionary<string, string> languageWorkersSettings)
        {
            if (languageWorkersSettings.TryGetValue($"{RpcWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}:{WorkerConstants.WorkerDescriptionDefaultExecutablePath}", out string defaultExecutablePathSetting) && defaultExecutablePathSetting is not null)
            {
                workerDescription.DefaultExecutablePath = defaultExecutablePathSetting;
            }

            if (languageWorkersSettings.TryGetValue($"{RpcWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}:{WorkerConstants.WorkerDescriptionDefaultRuntimeVersion}", out string defaultRuntimeVersionAppSetting) && defaultRuntimeVersionAppSetting is not null)
            {
                workerDescription.DefaultRuntimeVersion = defaultRuntimeVersionAppSetting;
            }
        }

        internal static void AddArgumentsFromAppSettings(RpcWorkerDescription workerDescription, ImmutableDictionary<string, string> languageWorkersSettings)
        {
            if (languageWorkersSettings.TryGetValue($"{RpcWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}:{WorkerConstants.WorkerDescriptionArguments}", out string argumentsValue) && argumentsValue is not null)
            {
                ((List<string>)workerDescription.Arguments).AddRange(Regex.Split(argumentsValue, @"\s+"));
            }
        }

        /// <summary>
        /// Determines if the worker directory should be skipped based on the current worker runtime and environment settings.
        /// </summary>
        internal static bool ShouldSkipWorkerDirectory(string workerRuntime, string workerDir, bool isMultiLanguageWorkerEnvironment, bool isPlaceholderModeEnabled)
        {
            return !isMultiLanguageWorkerEnvironment &&
                    !isPlaceholderModeEnabled &&
                    workerRuntime is not null &&
                    !workerRuntime.Equals(workerDir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if the required worker config path is found.
        /// </summary>
        internal static bool FoundWorkerConfigPath(string workerRuntime, Dictionary<string, RpcWorkerConfig> runtimeToConfigPathMap, bool isMultiLanguageWorkerEnvironment, bool isPlaceholderModeEnabled)
        {
            return !isMultiLanguageWorkerEnvironment &&
                    !isPlaceholderModeEnabled &&
                    !string.IsNullOrWhiteSpace(workerRuntime) &&
                    runtimeToConfigPathMap.ContainsKey(workerRuntime);
        }
    }
}