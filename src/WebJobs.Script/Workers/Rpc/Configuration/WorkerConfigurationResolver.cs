// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.AppService.Proxy.Common.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IEnvironment _environment;
        private readonly HashSet<string> _workersAvailableForResolutionViaHostingConfig;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        public WorkerConfigurationResolver(IConfiguration config,
                                        ILogger logger,
                                        IEnvironment environment,
                                        IWorkerProfileManager workerProfileManager,
                                        HashSet<string> workersAvailableForResolutionViaHostingConfig)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workersAvailableForResolutionViaHostingConfig = workersAvailableForResolutionViaHostingConfig;
        }

        public List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath)
        {
            // Dictionary of { FUNCTIONS_WORKER_RUNTIME environment variable value : path of workerConfig }
            ConcurrentDictionary<string, string> outputDict = new ConcurrentDictionary<string, string>();

            var workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            string releaseChannel = Utility.GetPlatformReleaseChannel(_environment);

            if (!probingPaths.IsNullOrEmpty())
            {
                // probing path directory structure is: <probingPath>/<workerRuntime>/<version>/<worker.config.json>
                foreach (var probingPath in probingPaths)
                {
                    if (!string.IsNullOrEmpty(probingPath) && Directory.Exists(probingPath))
                    {
                        foreach (var languageWorkerPath in Directory.EnumerateDirectories(probingPath))
                        {
                            string languageWorkerDir = Path.GetFileName(languageWorkerPath).ToLower();

                            // If probing paths are malformed and have duplicate directories of the same language worker (eg. due to different casing)
                            if (outputDict.ContainsKey(languageWorkerDir))
                            {
                                continue;
                            }

                            // Only skip worker directories that don't match the current runtime or are not enabled via hosting config
                            // Do not skip non-worker directories like the function app payload directory
                            if (languageWorkerPath.StartsWith(fallbackPath) || languageWorkerPath.StartsWith(probingPath))
                            {
                                if ((_workersAvailableForResolutionViaHostingConfig is not null &&
                                    !_workersAvailableForResolutionViaHostingConfig.Contains(languageWorkerDir)) ||
                                    (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                                    workerRuntime is not null &&
                                    !workerRuntime.Equals(languageWorkerDir, StringComparison.OrdinalIgnoreCase)))
                                {
                                    continue;
                                }
                            }

                            IEnumerable<string> workerVersions = Directory.EnumerateDirectories(languageWorkerPath);
                            var versions = ParseWorkerVersionsInDescending(workerVersions);

                            GetWorkerConfigsFromProbingPaths(versions, languageWorkerPath, languageWorkerDir, releaseChannel, outputDict);
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

            // Search in fallback path if worker cannot be found in probing paths
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

                if (IsWorkerCompatibleWithHost(languageWorkerVersionPath))
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
            if (Directory.Exists(fallbackPath))
            {
                foreach (var workerPath in Directory.EnumerateDirectories(fallbackPath))
                {
                    string workerDir = Path.GetFileName(workerPath).ToLower();

                    if (workerPath.StartsWith(fallbackPath))
                    {
                        if (outputDict.ContainsKey(workerDir) ||
                        (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                        workerRuntime is not null &&
                        !workerRuntime.Equals(workerDir, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }

                    string workerConfigPath = Path.Combine(workerPath, RpcWorkerConstants.WorkerConfigFileName);
                    if (File.Exists(workerConfigPath))
                    {
                        outputDict[workerDir] = workerPath;
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

        private static IEnumerable<Version> ParseWorkerVersionsInDescending(IEnumerable<string> workerVersions)
        {
            var versions = new List<Version>();

            foreach (var workerVersion in workerVersions)
            {
                string versionDir = Path.GetFileName(workerVersion);
                string formattedVersion = FormatVersion(versionDir);

                if (Version.TryParse(formattedVersion, out Version version))
                {
                    versions.Add(version);
                }
            }

            return versions.OrderDescending();
        }

        private static string FormatVersion(string version)
        {
            if (!version.Contains('.'))
            {
                version = version + ".0"; // Handle versions like '1' as '1.0'
            }

            return version;
        }

        private bool IsWorkerCompatibleWithHost(string workerDir)
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

            if (workerConfig.TryGetProperty(RpcWorkerConstants.HostRequirementsSectionName, out JsonElement configSection))
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
            HashSet<string> hostCapabilities = ScriptConstants.HostCapabilities;
            HashSet<string> hostRequirements = GetHostRequirements(workerConfig);

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