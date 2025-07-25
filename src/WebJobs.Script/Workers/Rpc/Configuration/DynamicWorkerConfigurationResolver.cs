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
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    // This class resolves worker configurations dynamically based on the current environment and configuration settings.
    // It searches for worker configs in specified probing paths and the fallback path, and returns a list of worker configuration paths.
    internal sealed class DynamicWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IFileSystem _fileSystem;
        private readonly HashSet<string> _workersAvailableForResolutionViaHostingConfig;
        private readonly List<string> _workerProbingPaths;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;

        public DynamicWorkerConfigurationResolver(ILogger logger,
                                        IFileSystem fileSystem,
                                        IWorkerProfileManager workerProfileManager,
                                        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigResolverOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workerConfigurationResolverOptions = workerConfigResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigResolverOptions));
            _workerProbingPaths = workerConfigResolverOptions.CurrentValue.ProbingPaths;
            _workersAvailableForResolutionViaHostingConfig = workerConfigResolverOptions.CurrentValue.WorkersAvailableForResolution ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<string> GetWorkerConfigPaths()
        {
            // Dictionary of { FUNCTIONS_WORKER_RUNTIME environment variable value : path of workerConfig }
            // Example: outputDict = {"java": "path1", "node": "path2", "dotnet-isolated": "path3"} for multilanguage worker scenario
            // Sample path: "<rootProbingPath>/<workerRuntimeDir>/<workerVersion>/"
            var outputDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var workerRuntime = _workerConfigurationResolverOptions.CurrentValue.WorkerRuntime;

            // Search for worker configs in probing paths
            ResolveWorkerConfigsFromProbingPaths(workerRuntime, outputDict);

            if (!_workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment &&
                !_workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled &&
                workerRuntime is not null &&
                outputDict.ContainsKey(workerRuntime))
            {
                return outputDict.Values.ToList();
            }

            // Search in fallback path if worker cannot be found in probing paths
            ResolveWorkerConfigsFromWithinHost(workerRuntime, outputDict);

            return outputDict.Values.ToList();
        }

        private void ResolveWorkerConfigsFromProbingPaths(string workerRuntime, Dictionary<string, string> outputDict)
        {
            _logger.LogDebug("Workers probing paths set to: {probingPaths}", _workerProbingPaths is null ? null : string.Join(", ", _workerProbingPaths));

            if (_workerProbingPaths is null)
            {
                return;
            }

            string releaseChannel = _workerConfigurationResolverOptions.CurrentValue.ReleaseChannel;

            // probing path directory structure is: <probingPath>/<workerRuntimeDir>/<workerVersion>/<worker.config.json>
            foreach (var probingPath in _workerProbingPaths)
            {
                if (string.IsNullOrWhiteSpace(probingPath))
                {
                    continue;
                }

                if (!_fileSystem.Directory.Exists(probingPath))
                {
                    _logger.LogDebug("Worker probing path directory does not exist: {probingPath}", probingPath);
                    continue;
                }

                foreach (var workerRuntimePath in _fileSystem.Directory.EnumerateDirectories(probingPath))
                {
                    string workerRuntimeDir = Path.GetFileName(workerRuntimePath);

                    // If probing paths are malformed and have duplicate directories of the same language worker (eg. due to different casing)
                    if (outputDict.ContainsKey(workerRuntimeDir))
                    {
                        continue;
                    }

                    // Skip worker directories that don't match the current runtime or are not enabled via hosting config
                    // Do not load all worker directories after the specialization is done and if it is not a multi-language runtime environment
                    if (!_workersAvailableForResolutionViaHostingConfig.Contains(workerRuntimeDir) ||
                            (!_workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment &&
                            !_workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled &&
                            ShouldSkipWorkerDirectory(workerRuntime, workerRuntimeDir)))
                    {
                        continue;
                    }

                    ResolveWorkerConfigsFromVersionsDirs(workerRuntimePath, workerRuntimeDir, releaseChannel, outputDict);
                }
            }
        }

        private void ResolveWorkerConfigsFromVersionsDirs(string languageWorkerPath, string languageWorkerFolder, string releaseChannel, Dictionary<string, string> outputDict)
        {
            var workerVersionPaths = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);

            // Map of: (parsed worker version, worker path)
            // Example: [ (1.0.0, "<rootProbingPath>/java/1.0.0"), (2.0.0, "<rootProbingPath>/java/2.0.0") ]
            var versionPathMap = GetWorkerVersionsDescending(workerVersionPaths);

            int compatibleWorkerCount = 0;

            bool isStandardOrExtendedChannel =
                        !string.IsNullOrWhiteSpace(releaseChannel) &&
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
                        return; // latest version is the default
                    }

                    if (compatibleWorkerCount > 1)
                    {
                        outputDict[languageWorkerFolder] = languageWorkerVersionPath;
                        return;
                    }
                }
            }
        }

        private void ResolveWorkerConfigsFromWithinHost(string workerRuntime, Dictionary<string, string> outputDict)
        {
            var fallbackPath = _workerConfigurationResolverOptions.CurrentValue.WorkersDirPath;

            _logger.LogDebug("Searching for worker configs in the fallback directory: {fallbackPath}", fallbackPath);

            if (!string.IsNullOrEmpty(fallbackPath) && _fileSystem.Directory.Exists(fallbackPath))
            {
                foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(fallbackPath))
                {
                    string workerDir = Path.GetFileName(workerPath).ToLower();

                    if (outputDict.ContainsKey(workerDir) ||
                        (!_workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment &&
                        !_workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled &&
                        ShouldSkipWorkerDirectory(workerRuntime, workerDir)))
                    {
                        continue;
                    }

                    string workerConfigPath = Path.Combine(workerPath, RpcWorkerConstants.WorkerConfigFileName);
                    if (File.Exists(workerConfigPath))
                    {
                        outputDict[workerDir] = workerPath;
                    }

                    if (!_workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment &&
                        !_workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled &&
                        workerRuntime is not null &&
                        outputDict.ContainsKey(workerRuntime))
                    {
                        return;
                    }
                }
            }
        }

        private SortedList<Version, string> GetWorkerVersionsDescending(IEnumerable<string> workerVersionPaths)
        {
            // Map of: (parsed worker version, worker path)
            // Example: [ (1.0.0, "<rootProbingPath>/java/1.0.0"), (2.0.0, "<rootProbingPath>/java/2.0.0") ]
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
            var workerDescription = WorkerConfigurationHelper.GetWorkerDescription(
                workerConfig: workerConfig,
                jsonSerializerOptions: _jsonSerializerOptions,
                workerDir: workerDir,
                profileManager: _profileManager,
                configSection: _workerConfigurationResolverOptions.CurrentValue.LanguageSection,
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

        internal static bool ShouldSkipWorkerDirectory(string workerRuntime, string workerDir)
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