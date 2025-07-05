// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class DynamicWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IEnvironment _environment;
        private readonly IFileSystem _fileSystem;
        private readonly HashSet<string> _workersAvailableForResolutionViaHostingConfig;
        private readonly List<string> _workerProbingPaths;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        public DynamicWorkerConfigurationResolver(IConfiguration config,
                                        ILogger logger,
                                        IEnvironment environment,
                                        IFileSystem fileSystem,
                                        IWorkerProfileManager workerProfileManager,
                                        HashSet<string> workersAvailableForResolutionViaHostingConfig,
                                        List<string> workerProbingPaths)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workersAvailableForResolutionViaHostingConfig = workersAvailableForResolutionViaHostingConfig;
            _workerProbingPaths = workerProbingPaths;
        }

        public List<string> GetWorkerConfigs()
        {
            string probingPaths = _workerProbingPaths is not null ? string.Join(", ", _workerProbingPaths) : null;
            _logger.LogDebug("Workers probing paths set to: {probingPaths}", probingPaths);

            var workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            string releaseChannel = Utility.GetPlatformReleaseChannel(_environment);

            // Dictionary of { FUNCTIONS_WORKER_RUNTIME environment variable value : path of workerConfig }
            // Example: outputDict = {"java": "path1", "node": "path2", "dotnet-isolated": "path3"} for multilanguage worker scenario
            Dictionary<string, string> outputDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_workerProbingPaths is not null)
            {
                // probing path directory structure is: <probingPath>/<workerRuntimeDir>/<workerVersion>/<worker.config.json>
                foreach (var probingPath in _workerProbingPaths)
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

                            bool workerUnavailableViaHostingConfig = _workersAvailableForResolutionViaHostingConfig is not null && !_workersAvailableForResolutionViaHostingConfig.Contains(workerRuntimeDir);

                            // Skip worker directories that don't match the current runtime or are not enabled via hosting config
                            if (workerUnavailableViaHostingConfig ||
                                    (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                                    !_environment.IsPlaceholderModeEnabled() &&
                                    WorkerConfigurationHelper.ShouldSkipRuntime(workerRuntime, workerRuntimeDir)))
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

            // Search in fallback path if worker cannot be found in probing paths
            PopulateWorkerConfigsFromWithinHost(workerRuntime, outputDict);

            return outputDict.Values.ToList();
        }

        private void PopulateWorkerConfigsFromProbingPaths(string languageWorkerPath, string languageWorkerFolder, string releaseChannel, Dictionary<string, string> outputDict)
        {
            var workerVersionPaths = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);

            // Map of: (parsed worker version, worker path)
            var versionPathMap = GetWorkerVersionsDescending(workerVersionPaths);

            int compatibleWorkerCount = 0;

            bool isStandardOrExtendedChannel =
                        releaseChannel != null &&
                        (releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper, StringComparison.OrdinalIgnoreCase) ||
                        releaseChannel.Equals(ScriptConstants.ExtendedPlatformChannelNameUpper, StringComparison.OrdinalIgnoreCase));

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

        private void PopulateWorkerConfigsFromWithinHost(string workerRuntime, Dictionary<string, string> outputDict)
        {
            var fallbackPath = WorkerConfigurationHelper.GetWorkersDirPath(_config);

            _logger.LogDebug("Searching for worker configs in the fallback directory: {fallbackPath}", fallbackPath);

            if (fallbackPath != null && _fileSystem.Directory.Exists(fallbackPath))
            {
                foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(fallbackPath))
                {
                    string workerDir = Path.GetFileName(workerPath).ToLower();

                    if (outputDict.ContainsKey(workerDir) ||
                            (!_environment.IsMultiLanguageRuntimeEnvironment() &&
                            WorkerConfigurationHelper.ShouldSkipRuntime(workerRuntime, workerDir)))
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
            // Map of: (parsed worker version, worker path)
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

            if (workerConfig.ValueKind == JsonValueKind.Undefined)
            {
                return false;
            }

            // static capability resolution
            bool hostHasRequiredCapabilities = DoesHostHasRequiredCapabilities(workerConfig);

            if (!hostHasRequiredCapabilities)
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
                logger: _logger);

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
        private HashSet<string> GetHostRequirementsFromWorker(JsonElement workerConfig)
        {
            HashSet<string> hostRequirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        private bool DoesHostHasRequiredCapabilities(JsonElement workerConfig)
        {
            HashSet<string> hostCapabilities = ScriptConstants.HostCapabilities;
            HashSet<string> hostRequirements = GetHostRequirementsFromWorker(workerConfig);

            foreach (var hostRequirement in hostRequirements)
            {
                if (!hostCapabilities.Contains(hostRequirement))
                {
                    return false;
                }
            }

            return true;
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