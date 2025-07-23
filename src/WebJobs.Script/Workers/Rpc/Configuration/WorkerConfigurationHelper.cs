// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal static class WorkerConfigurationHelper
    {
        internal static RpcWorkerDescription GetWorkerDescription(
            JsonElement workerConfig,
            JsonSerializerOptions jsonSerializerOptions,
            string workerDir,
            IWorkerProfileManager profileManager,
            IConfiguration configSection,
            ILogger logger)
        {
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

            // Check if any app settings are provided for that language
            var languageSection = configSection?.GetSection($"{workerDescription.Language}");
            workerDescription.Arguments ??= new List<string>();
            GetWorkerDescriptionFromAppSettings(workerDescription, languageSection);
            AddArgumentsFromAppSettings(workerDescription, languageSection);

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

        private static void GetWorkerDescriptionFromAppSettings(RpcWorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            var defaultExecutablePathSetting = languageSection?.GetSection($"{WorkerConstants.WorkerDescriptionDefaultExecutablePath}");
            workerDescription.DefaultExecutablePath = defaultExecutablePathSetting?.Value != null ? defaultExecutablePathSetting.Value : workerDescription.DefaultExecutablePath;

            var defaultRuntimeVersionAppSetting = languageSection?.GetSection($"{WorkerConstants.WorkerDescriptionDefaultRuntimeVersion}");
            workerDescription.DefaultRuntimeVersion = defaultRuntimeVersionAppSetting?.Value != null ? defaultRuntimeVersionAppSetting.Value : workerDescription.DefaultRuntimeVersion;
        }

        internal static void AddArgumentsFromAppSettings(RpcWorkerDescription workerDescription, IConfigurationSection languageSection)
        {
            var argumentsSection = languageSection?.GetSection($"{WorkerConstants.WorkerDescriptionArguments}");
            if (argumentsSection?.Value != null)
            {
                ((List<string>)workerDescription.Arguments).AddRange(Regex.Split(argumentsSection.Value, @"\s+"));
            }
        }

        internal static string GetDefaultWorkersDirectory(Func<string, bool> directoryExists)
        {
#pragma warning disable SYSLIB0012 // Type or member is obsolete
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath);
#pragma warning restore SYSLIB0012 // Type or member is obsolete
            string workersDirPath = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            if (!directoryExists(workersDirPath))
            {
                // Site Extension. Default to parent directory
                workersDirPath = Path.Combine(Directory.GetParent(assemblyLocalPath).FullName, RpcWorkerConstants.DefaultWorkersDirectoryName);
            }
            return workersDirPath;
        }

        internal static string GetWorkersDirPath(IConfiguration configuration)
        {
            var workersDirectorySection = configuration?.GetSection($"{WorkerConstants.WorkersDirectorySectionName}");

            string workersDirPath = GetDefaultWorkersDirectory(Directory.Exists);

            if (!string.IsNullOrEmpty(workersDirectorySection?.Value))
            {
                workersDirPath = workersDirectorySection.Value;
            }

            return workersDirPath;
        }
    }
}