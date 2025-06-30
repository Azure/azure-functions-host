// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
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
        private readonly IFileSystem _fileSystem;
        private readonly HashSet<string> _workersAvailableForResolutionViaHostingConfig;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        public WorkerConfigurationResolver(IConfiguration config,
                                        ILogger logger,
                                        IEnvironment environment,
                                        IFileSystem fileSystem,
                                        IWorkerProfileManager workerProfileManager,
                                        HashSet<string> workersAvailableForResolutionViaHostingConfig)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workersAvailableForResolutionViaHostingConfig = workersAvailableForResolutionViaHostingConfig ?? throw new ArgumentNullException(nameof(workersAvailableForResolutionViaHostingConfig));
        }

        public List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath)
        {
            // Dictionary of { FUNCTIONS_WORKER_RUNTIME environment variable value : path of workerConfig }
            Dictionary<string, string> outputDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            string releaseChannel = Utility.GetPlatformReleaseChannel(_environment);

            if (!probingPaths.IsNullOrEmpty())
            {
                // probing path directory structure is: <probingPath>/<workerRuntimeDir>/<workerVersion>/<worker.config.json>
                foreach (var probingPath in probingPaths)
                {
                    if (!string.IsNullOrEmpty(probingPath) && _fileSystem.Directory.Exists(probingPath))
                    {
                        foreach (var workerRuntimePath in _fileSystem.Directory.EnumerateDirectories(probingPath))
                        {
                            string workerRuntimeDir = Path.GetFileName(workerRuntimePath);

                            // If probing paths are malformed and have duplicate directories of the same language worker (eg. due to different casing)
                            if (outputDict.ContainsKey(workerRuntimeDir))
                            {
                                continue;
                            }

                            // Only skip worker directories that don't match the current runtime or are not enabled via hosting config
                            if (!_workersAvailableForResolutionViaHostingConfig.Contains(workerRuntimeDir) ||
                                    (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                                    !_environment.IsPlaceholderModeEnabled() &&
                                    IsRequiredWorkerRuntime(workerRuntime, workerRuntimeDir)))
                            {
                                continue;
                            }

                            PopulateWorkerConfigsFromProbingPaths(workerRuntimePath, workerRuntimeDir, releaseChannel, outputDict);
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

            _logger.LogDebug("Searching for worker configs in the fallback directory.");

            // Search in fallback path if worker cannot be found in probing paths
            PopulateWorkerConfigsFromWithinHost(fallbackPath, workerRuntime, outputDict);

            return outputDict.Values.ToList();
        }

        private void PopulateWorkerConfigsFromProbingPaths(string languageWorkerPath, string languageWorkerFolder, string releaseChannel, Dictionary<string, string> outputDict)
        {
            var versionsDir = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);
            var versionPathMap = GetWorkerVersionsDescending(versionsDir);

            int compatibleWorkerCount = 0;

            bool isStandardOrExtendedChannel =
                        releaseChannel != null &&
                        (releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper) ||
                        releaseChannel.Equals(ScriptConstants.ExtendedPlatformChannelNameUpper));

            // language worker version
            foreach (var versionPair in versionPathMap)
            {
                string languageWorkerVersionPath = versionPair.Value;

                if (IsWorkerCompatibleWithHost(languageWorkerVersionPath))
                {
                    compatibleWorkerCount++;
                    outputDict[languageWorkerFolder] = languageWorkerVersionPath;

                    if (string.IsNullOrEmpty(releaseChannel) || !isStandardOrExtendedChannel)
                    {
                        break; // latest version is the default
                    }

                    if (compatibleWorkerCount > 1 && isStandardOrExtendedChannel)
                    {
                        outputDict[languageWorkerFolder] = languageWorkerVersionPath;
                        break;
                    }
                }
            }
        }

        private void PopulateWorkerConfigsFromWithinHost(string fallbackPath, string workerRuntime, Dictionary<string, string> outputDict)
        {
            if (_fileSystem.Directory.Exists(fallbackPath))
            {
                foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(fallbackPath))
                {
                    string workerDir = Path.GetFileName(workerPath).ToLower();

                    if (outputDict.ContainsKey(workerDir) ||
                            (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                            IsRequiredWorkerRuntime(workerRuntime, workerDir)))
                    {
                        continue;
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

        private SortedList<Version, string> GetWorkerVersionsDescending(IEnumerable<string> workerVersionPaths)
        {
            var versionPathMap = new SortedList<Version, string>(new DescendingVersionComparer());

            if (!workerVersionPaths.Any())
            {
                return versionPathMap;
            }

            foreach (var workerVersionPath in workerVersionPaths)
            {
                string versionDir = Path.GetFileName(workerVersionPath);
                string formattedVersion = FormatVersion(versionDir);

                if (Version.TryParse(formattedVersion, out Version version))
                {
                    versionPathMap[version] = workerVersionPath;
                }
                else
                {
                    _logger.LogTrace("Failed to parse worker version '{versionDir}' as a valid version.", versionDir);
                }
            }

            return versionPathMap;
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
                workerConfig: workerConfig,
                jsonSerializerOptions: _jsonSerializerOptions,
                workerDir: workerDir,
                profileManager: _profileManager,
                config: _config,
                logger: _logger,
                dynamicWorkerResolutionEnabled: true,
                workersAvailableForResolutionViaHostingConfig: _workersAvailableForResolutionViaHostingConfig);

            if (workerDescription.IsDisabled == true)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts host requirements from the worker configuration JSON element.
        /// </summary>
        /// <param name="workerConfig"> Worker config: { "hostRequirements": [ "test-capability1", "test-capability2" ] }. </param>
        /// <returns> HashSet { "test-capability1", "test-capability2" }. </returns>
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

        private bool IsRequiredWorkerRuntime(string workerRuntime, string workerDir)
        {
            return workerRuntime is not null && !workerRuntime.Equals(workerDir, StringComparison.OrdinalIgnoreCase);
        }

        private string FormatVersion(string version)
        {
            if (!version.Contains('.'))
            {
                version = version + ".0"; // Handle versions like '1' as '1.0'
            }

            return version;
        }

        private class DescendingVersionComparer : IComparer<Version>
        {
            public int Compare(Version x, Version y)
            {
                return y.CompareTo(x); // Inverted comparison for descending order
            }
        }
    }
}