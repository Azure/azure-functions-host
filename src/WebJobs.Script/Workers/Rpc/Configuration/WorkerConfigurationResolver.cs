// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.AppService.Proxy.Common.Extensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IEnvironment _environment;
        private readonly FunctionsHostingConfigOptions _functionsHostingConfigOptions;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private HashSet<string> _probingPathsEnabledWorkersViaHostingConfig;

        public WorkerConfigurationResolver(IConfiguration config,
                                        ILogger logger,
                                        IEnvironment environment,
                                        IWorkerProfileManager workerProfileManager,
                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _functionsHostingConfigOptions = functionsHostingConfigOptions?.Value ?? throw new ArgumentNullException(nameof(functionsHostingConfigOptions));

            _probingPathsEnabledWorkersViaHostingConfig = _functionsHostingConfigOptions.EnableProbingPathsForWorkers.ToLowerInvariant().Split("|").ToHashSet();
        }

        public List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath)
        {
            var workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

            // Dictionary of language-name : workerConfig
            ConcurrentDictionary<string, string> outputDict = new ConcurrentDictionary<string, string>();

            // check worker release channel
            string releaseChannel = Utility.GetPlatformReleaseChannel(_environment);

            if (!probingPaths.IsNullOrEmpty())
            {
                foreach (var probingPath in probingPaths)
                {
                    if (!string.IsNullOrEmpty(probingPath) && Directory.Exists(probingPath))
                    {
                        foreach (var languageWorkerPath in Directory.EnumerateDirectories(probingPath))
                        {
                            string languageWorkerFolder = Path.GetFileName(languageWorkerPath);

                            _logger.LogInformation("Probing for language worker in path: {LanguageWorkerPath}", languageWorkerPath);

                            // Only skip worker directories that don't match the current runtime.
                            // Do not skip non-worker directories like the function app payload directory
                            // && languageWorkerPath.StartsWith(fallbackPath))
                            if (//!_probingPathsEnabledWorkersViaHostingConfig.Contains(languageWorkerFolder) ||
                                (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                                workerRuntime is not null &&
                                !workerRuntime.Equals(languageWorkerFolder, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            IEnumerable<string> workerVersions = Directory.EnumerateDirectories(languageWorkerPath);
                            var versionsList = ParseWorkerVersions(workerVersions);
                            var versions = versionsList.OrderDescending();

                            if (outputDict.ContainsKey(languageWorkerFolder))
                            {
                                continue;
                            }

                            GetWorkerConfigsFromProbingPaths(versions, languageWorkerPath, languageWorkerFolder, releaseChannel, outputDict);

                            _logger.LogInformation("Found worker config for {LanguageWorkerFolder} at {LanguageWorkerPath}", languageWorkerFolder, outputDict.GetValueOrDefault(languageWorkerFolder));
                        }
                    }
                }
            }

            if (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                workerRuntime is not null &&
                outputDict.ContainsKey(workerRuntime))
            {
                return outputDict.Values.ToList();
            }

            // fallback path
            GetWorkerConfigsFromWithinHost(fallbackPath, workerRuntime, outputDict);

            return outputDict.Values.ToList();
        }

        private void GetWorkerConfigsFromProbingPaths(IEnumerable<Version> versions, string languageWorkerPath, string languageWorkerFolder, string releaseChannel, ConcurrentDictionary<string, string> outputDict)
        {
            int found = 0;

            // language worker version
            foreach (Version versionFolder in versions)
            {
                string languageWorkerVersionPath = Path.Combine(languageWorkerPath, versionFolder.ToString());
                if (IsCompatibleWithHost(languageWorkerVersionPath))
                {
                    found++;
                    outputDict[languageWorkerFolder] = languageWorkerVersionPath;

                    if (string.IsNullOrEmpty(releaseChannel) || !releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper))
                    {
                        // latest version is the default
                        break;
                    }

                    if (found > 1 && releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper))
                    {
                        outputDict[languageWorkerFolder] = languageWorkerVersionPath;
                        break;
                    }
                }
            }
        }

        private void GetWorkerConfigsFromWithinHost(string fallbackPath, string workerRuntime, ConcurrentDictionary<string, string> outputDict)
        {
            _logger.LogInformation("Searching for worker configs in fallback path: {FallbackPath}", fallbackPath);

            // fallback path
            if (Directory.Exists(fallbackPath))
            {
                foreach (var workerDir in Directory.EnumerateDirectories(fallbackPath))
                {
                    string workerFolder = Path.GetFileName(workerDir);

                    if (outputDict.ContainsKey(workerFolder) ||
                        (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                        workerRuntime is not null &&
                        !workerRuntime.Equals(workerFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
                    if (File.Exists(workerConfigPath))
                    {
                        outputDict[workerFolder] = workerDir;
                        _logger.LogInformation("Found worker in fallback path workerfolder = {workerFolder} and dir = {workerDir}", workerFolder, workerDir);
                    }

                    if (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                        workerRuntime is not null &&
                        outputDict.ContainsKey(workerRuntime))
                    {
                        break;
                    }
                }
            }
        }

        private List<Version> ParseWorkerVersions(IEnumerable<string> workerVersions)
        {
            var versions = new List<Version>();

            foreach (var workerVersion in workerVersions)
            {
                string versionFolder = Path.GetFileName(workerVersion);

                if (versionFolder.Length == 1)
                {
                    versionFolder = versionFolder + ".0"; // Handle single digit versions like '1' as '1.0'
                }

                if (Version.TryParse(versionFolder, out Version version))
                {
                    versions.Add(version);
                }
                else
                {
                    Console.WriteLine($"Failed to parse version: '{versionFolder}'");
                }
            }

            return versions;
        }

        private HashSet<string> GetHostCapabilities()
        {
            HashSet<string> hostCapabilites = ["test-capability-1", "test-capability-2"];

            return hostCapabilites;
        }

        private bool IsCompatibleWithHost(string workerDir)
        {
            string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
            if (!File.Exists(workerConfigPath))
            {
                return false;
            }

            JsonElement workerConfig = WorkerConfigurationHelper.GetWorkerConfigJsonElement(workerConfigPath);

            // static capability resolution
            bool doesHostRequirementMeet = DoesHostRequirementMeet(workerConfig);

            if (!doesHostRequirementMeet)
            {
                return false;
            }

            // profiles evaluation
            RpcWorkerDescription workerDescription = WorkerConfigurationHelper.GetWorkerDescription(
                workerConfig,
                _jsonSerializerOptions,
                workerDir,
                true,
                _profileManager,
                _config,
                _logger);

            if (workerDescription.IsDisabled == true)
            {
                return false;
            }

            return true;
        }

        private HashSet<string> GetHostRequirements(JsonElement workerConfig)
        {
            HashSet<string> hostRequirements = new HashSet<string>();

            if (workerConfig.TryGetProperty("hostRequirements", out JsonElement configSection))
            {
                var requirements = configSection.EnumerateArray();

                foreach (var requirement in requirements)
                {
                    hostRequirements.Add(requirement.GetString());
                }
            }

            return hostRequirements;
        }

        private bool DoesHostRequirementMeet(JsonElement workerConfig)
        {
            HashSet<string> hostRequirements = GetHostRequirements(workerConfig);

            HashSet<string> hostCapabilities = GetHostCapabilities();

            foreach (var hostRequirement in hostRequirements)
            {
                if (!hostCapabilities.Contains(hostRequirement))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
