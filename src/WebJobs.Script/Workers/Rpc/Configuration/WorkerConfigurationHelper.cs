// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal static class WorkerConfigurationHelper
    {
        internal static void AddProvider(WorkerConfigurationResolverOptions resolverOptions,
                                            string workerName,
                                            string workerDirPath,
                                            IMetricsLogger metricsLogger,
                                            IWorkerProfileManager profileManager,
                                            ILogger logger,
                                            ISystemRuntimeInformation systemRuntimeInformation,
                                            Dictionary<string, RpcWorkerConfig> workerRuntimeToConfigMap)
        {
            using (metricsLogger.LatencyEvent(string.Format(MetricEventNames.AddProvider, workerDirPath)))
            {
                if (workerRuntimeToConfigMap.ContainsKey(workerName))
                {
                    return;
                }

                // After specialization, load worker config only for the specified runtime unless it's a multi-language app.
                // Only skip worker directories that don't match the current runtime.
                // Do not skip non-worker directories like the function app payload directory
                if (ShouldSkipWorkerDirectory(resolverOptions.WorkerRuntime, Path.GetFileName(workerDirPath), resolverOptions.IsMultiLanguageWorkerEnvironment, resolverOptions.IsPlaceholderModeEnabled)
                    && workerDirPath.StartsWith(resolverOptions.WorkersRootDirPath))
                {
                    return;
                }

                (var workerDescription, var workerConfigJson) = GetWorkerDescriptionAndConfig(workerDirPath, profileManager, resolverOptions.WorkerDescriptionOverrides, logger);
                if (workerDescription is null || IsWorkerDescriptionDisabled(workerDescription, logger))
                {
                    return;
                }

                var workerConfig = BuildWorkerConfig(resolverOptions, workerDirPath, workerConfigJson, workerDescription, metricsLogger, logger, systemRuntimeInformation);
                if (workerConfig is not null)
                {
                    workerRuntimeToConfigMap[workerName] = workerConfig;
                }
            }
        }

        internal static RpcWorkerConfig BuildWorkerConfig(WorkerConfigurationResolverOptions resolverOptions,
                                                    string workerDir,
                                                    JsonElement workerConfig,
                                                    RpcWorkerDescription workerDescription,
                                                    IMetricsLogger metricsLogger,
                                                    ILogger logger,
                                                    ISystemRuntimeInformation systemRuntimeInformation)
        {
            try
            {
                var workerRuntime = resolverOptions.WorkerRuntime;

                if (ShouldAddWorkerConfig(workerDescription.Language, resolverOptions.IsPlaceholderModeEnabled, resolverOptions.IsMultiLanguageWorkerEnvironment, logger, workerRuntime))
                {
                    workerDescription.FormatWorkerPathIfNeeded(systemRuntimeInformation, workerRuntime, resolverOptions.FunctionsWorkerRuntimeVersion, logger);
                    workerDescription.FormatWorkingDirectoryIfNeeded();
                    workerDescription.FormatArgumentsIfNeeded(logger);
                    workerDescription.ThrowIfFileNotExists(workerDescription.DefaultWorkerPath, nameof(workerDescription.DefaultWorkerPath));
                    workerDescription.ExpandEnvironmentVariables();

                    WorkerProcessCountOptions workerProcessCount = GetWorkerProcessCount(workerConfig, resolverOptions.WorkerProcessCount, resolverOptions.EffectiveCoresCount);

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

            return null;
        }

        private static JsonElement GetWorkerConfigJsonElement(string workerConfigPath, ILogger logger)
        {
            ReadOnlySpan<byte> jsonSpan = File.ReadAllBytes(workerConfigPath);

            if (jsonSpan.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
            {
                jsonSpan = jsonSpan[3..]; // Skip UTF-8 Byte Order Mark (BOM) if present at the beginning of the file.
            }

            if (jsonSpan.IsEmpty)
            {
                logger.LogDebug("Worker config at '{workerConfigPath}' is empty.", workerConfigPath);
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

        internal static WorkerProcessCountOptions GetWorkerProcessCount(JsonElement workerConfig, string functionsWorkerProcessCount, int coresCount)
        {
            WorkerProcessCountOptions workerProcessCount = null;
            var jsonSerializerOptions = JsonSerializerOptionsProvider.CaseInsensitiveJsonSerializerOptions;

            if (workerConfig.TryGetProperty(WorkerConstants.ProcessCount, out var processCountElement))
            {
                workerProcessCount = processCountElement.Deserialize<WorkerProcessCountOptions>(jsonSerializerOptions);
            }

            workerProcessCount ??= new WorkerProcessCountOptions();

            if (workerProcessCount.SetProcessCountToNumberOfCpuCores)
            {
                workerProcessCount.ProcessCount = coresCount;
                // set Max worker process count to Number of effective cores if MaxProcessCount is less than MinProcessCount
                workerProcessCount.MaxProcessCount = workerProcessCount.ProcessCount > workerProcessCount.MaxProcessCount ? workerProcessCount.ProcessCount : workerProcessCount.MaxProcessCount;
            }

            // Env variable takes precedence over worker.config
            string processCountEnvSetting = functionsWorkerProcessCount;
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

        private static void GetWorkerDescriptionFromAppSettings(RpcWorkerDescription workerDescription, IReadOnlyDictionary<string, RpcWorkerDescription> workerDescriptionOverrides)
        {
            if (workerDescriptionOverrides.TryGetValue(workerDescription.Language, out var rpcWorkerDescription) && rpcWorkerDescription is not null)
            {
                workerDescription.DefaultExecutablePath = rpcWorkerDescription.DefaultExecutablePath ?? workerDescription.DefaultExecutablePath;
                workerDescription.DefaultRuntimeVersion = rpcWorkerDescription.DefaultRuntimeVersion ?? workerDescription.DefaultRuntimeVersion;
            }
        }

        private static void AddArgumentsFromAppSettings(RpcWorkerDescription workerDescription, IReadOnlyDictionary<string, RpcWorkerDescription> workerDescriptionOverrides)
        {
            if (workerDescriptionOverrides.TryGetValue(workerDescription.Language, out var rpcWorkerDescription) && rpcWorkerDescription?.Arguments is string[] args && args.Length > 0)
            {
                ((List<string>)workerDescription.Arguments).AddRange(args);
            }
        }

        internal static bool ShouldAddWorkerConfig(string workerDescriptionLanguage, bool placeholderModeEnabled, bool multiLanguageWorkerEnvironment, ILogger logger, string workerRuntime)
        {
            if (placeholderModeEnabled)
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
                logger.LogDebug("EnvironmentVariable {functionWorkerRuntimeSettingName}: {workerRuntime}", EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
                if (workerRuntime.Equals(workerDescriptionLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // After specialization only create worker provider for the language set by FUNCTIONS_WORKER_RUNTIME env variable
                logger.LogInformation("{FUNCTIONS_WORKER_RUNTIME} set to {workerRuntime}. Skipping WorkerConfig for language: {workerDescriptionLanguage}", EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime, workerDescriptionLanguage);
                return false;
            }

            return true;
        }

        private static void ReadLanguageWorkerFile(string workerPath, bool placeHolderModeEnabled, ILogger logger, string workerRuntime)
        {
            if (!placeHolderModeEnabled
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

        internal static (RpcWorkerDescription WorkerDescription, JsonElement WorkerConfig) GetWorkerDescriptionAndConfig(
            string workerDirPath,
            IWorkerProfileManager profileManager,
            IReadOnlyDictionary<string, RpcWorkerDescription> workerDescriptionOverrides,
            ILogger logger)
        {
            try
            {
                var workerConfigPath = Path.Combine(workerDirPath, RpcWorkerConstants.WorkerConfigFileName);
                if (!IsWorkerConfigPathValid(workerConfigPath, logger))
                {
                    return (null, default);
                }

                var workerConfig = GetWorkerConfigJsonElement(workerConfigPath, logger);
                if (workerConfig.ValueKind == JsonValueKind.Undefined)
                {
                    return (null, default);
                }

                var jsonSerializerOptions = JsonSerializerOptionsProvider.CaseInsensitiveJsonSerializerOptions;
                var workerDescriptionElement = workerConfig.GetProperty(WorkerConstants.WorkerDescription);
                var workerDescription = workerDescriptionElement.Deserialize<RpcWorkerDescription>(jsonSerializerOptions);
                workerDescription.WorkerDirectory = workerDirPath;

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

                // Check if any app settings are provided for that language
                GetWorkerDescriptionFromAppSettings(workerDescription, workerDescriptionOverrides);
                AddArgumentsFromAppSettings(workerDescription, workerDescriptionOverrides);

                // Validate workerDescription
                workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), logger);

                return (workerDescription, workerConfig);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                logger.LogError(ex, "Failed to initialize worker provider for: {workerDir}", workerDirPath);
            }

            return (null, default);
        }

        /// <summary>
        /// Determines if the worker directory should be skipped based on the current worker runtime and environment settings.
        /// </summary>
        internal static bool ShouldSkipWorkerDirectory(string workerRuntime, string workerDir, bool isMultiLanguageEnv, bool isPlaceholderMode)
        {
            // After specialization, load worker config only for the specified runtime unless it's a multi-language app.
            // Skip worker directories that don't match the current runtime.
            return !isMultiLanguageEnv &&
                    !isPlaceholderMode &&
                    !string.IsNullOrWhiteSpace(workerRuntime) &&
                    !workerRuntime.Equals(workerDir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if the specified worker configuration file exists at the given path.
        /// </summary>
        private static bool IsWorkerConfigPathValid(string workerConfigPath, ILogger logger)
        {
            if (!File.Exists(workerConfigPath))
            {
                logger.LogDebug("Did not find worker config file at: {workerConfigPath}", workerConfigPath);
                return false;
            }

            logger.LogDebug("Found worker config: {workerConfigPath}", workerConfigPath);

            return true;
        }

        /// <summary>
        /// Determines if the specified worker description is disabled.
        /// </summary>
        internal static bool IsWorkerDescriptionDisabled(RpcWorkerDescription workerDescription, ILogger logger)
        {
            if (workerDescription.IsDisabled == true)
            {
                logger.LogInformation("Skipping WorkerConfig for stack: {language} since it is disabled.", workerDescription.Language);
                return true;
            }

            return false;
        }
    }
}