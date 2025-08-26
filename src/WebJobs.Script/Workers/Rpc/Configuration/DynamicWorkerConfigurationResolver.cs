// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
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
        private readonly HashSet<string> _workersAvailableForResolution;
        private readonly List<string> _workerProbingPaths;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;
        private readonly Dictionary<string, HashSet<Version>> _ignoredVersions;

        public DynamicWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                    IFileSystem fileSystem,
                                                    IWorkerProfileManager workerProfileManager,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workerConfigurationResolverOptions = workerConfigResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigResolverOptions));
            ArgumentNullException.ThrowIfNull(workerConfigResolverOptions.CurrentValue);
            _workerProbingPaths = workerConfigResolverOptions.CurrentValue.ProbingPaths;
            _workersAvailableForResolution = workerConfigResolverOptions.CurrentValue.WorkersAvailableForResolution;
            _ignoredVersions = workerConfigResolverOptions.CurrentValue.IgnoredWorkerVersions;
        }

        public WorkerConfigurationInfo GetConfigurationInfo()
        {
            return new WorkerConfigurationInfo(
                WorkersRootDirPath: _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                WorkerConfigPaths: GetWorkerConfigPaths(),
                LanguageWorkersSettings: _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings);
        }

        /// <summary>
        /// Gets the list of worker configuration paths by searching probing paths and fallback path.
        /// </summary>
        internal List<string> GetWorkerConfigPaths()
        {
            // Dictionary of { FUNCTIONS_WORKER_RUNTIME environment variable value : path of workerConfig }
            // outputDict example: {"java": "path1", "node": "path2", "dotnet-isolated": "path3"} for multilanguage worker scenario
            // path format: "<rootProbingPath>/<workerRuntimeDir>/<workerVersion>/"
            // path example: "c:\\home\\SiteExtensions\\functionsworkers\\java\\1.0.0"
            var outputDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var workerRuntime = _workerConfigurationResolverOptions.CurrentValue.WorkerRuntime;

            // Search for worker configs in probing paths
            ResolveWorkerConfigsFromProbingPaths(workerRuntime, outputDict);

            if (FoundWorkerConfigPath(workerRuntime, outputDict))
            {
                return outputDict.Values.ToList();
            }

            // Search in fallback path if worker cannot be found in probing paths
            ResolveWorkerConfigsFromWithinHost(workerRuntime, outputDict);

            return outputDict.Values.ToList();
        }

        /// <summary>
        /// Resolves worker configuration paths from the specified probing paths.
        /// </summary>
        private void ResolveWorkerConfigsFromProbingPaths(string workerRuntime, Dictionary<string, string> outputDict)
        {
            try
            {
                _logger.LogDebug("Worker probing paths set to: {probingPaths}", string.Join(", ", _workerProbingPaths));

                // Probing path directory structure is: <probingPath>/<workerRuntimeDir>/<workerVersion>/<worker.config.json>
                foreach (var probingPath in _workerProbingPaths)
                {
                    if (string.IsNullOrWhiteSpace(probingPath))
                    {
                        continue;
                    }

                    if (!_fileSystem.Directory.Exists(probingPath))
                    {
                        _logger.LogDebug("Worker probing path directory does not exist: {probingPath}.", probingPath);
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
                        // Do not load all workers after the specialization is done and if it is not a multi-language runtime environment
                        if (!_workersAvailableForResolution.Contains(workerRuntimeDir) || ShouldSkipWorkerDirectory(workerRuntime, workerRuntimeDir))
                        {
                            continue;
                        }

                        // Search for worker config inside version directories within the language worker directory
                        // Example workerVersionPath: "<rootProbingPath>/java/1.0.0"
                        var workerVersionPath = ResolveWorkerConfigFromVersionsDirs(workerRuntimePath, workerRuntimeDir);
                        if (!string.IsNullOrWhiteSpace(workerVersionPath))
                        {
                            outputDict[workerRuntimeDir] = workerVersionPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Catching exceptions such as unauthorized access, IO exception, path too long, that can happen while searching for configs in probing paths.
                // Logging the exception and continuing to search worker configs in the fallback path.
                _logger.LogError(ex, "Failed to resolve worker configurations from probing paths.");
            }
        }

        /// <summary>
        /// Resolves worker configuration paths from the version directories within a language worker directory.
        /// </summary>
        private string ResolveWorkerConfigFromVersionsDirs(string languageWorkerPath, string languageWorkerFolder)
        {
            var workerVersionPaths = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);

            // Map of: (parsed worker version, worker path)
            // Example: [ (1.0.0, "<rootProbingPath>/java/1.0.0"), (2.0.0, "<rootProbingPath>/java/2.0.0") ]
            var versionPathMap = GetWorkerVersionsDescending(workerVersionPaths);

            int compatibleWorkerCount = 0;
            string outputWorkerVersionPath = null;

            foreach (var versionPair in versionPathMap)
            {
                if (_ignoredVersions.TryGetValue(languageWorkerFolder, out HashSet<Version> value) && value.Contains(versionPair.Key))
                {
                    _logger.LogDebug("Ignoring {languageWorkerFolder} version {version} as per configuration.", languageWorkerFolder, versionPair.Key);
                    continue;
                }

                string languageWorkerVersionPath = versionPair.Value;

                if (IsWorkerCompatibleWithHost(languageWorkerVersionPath))
                {
                    compatibleWorkerCount++;
                    outputWorkerVersionPath = languageWorkerVersionPath;

                    if (!IsStandardOrExtendedChannel())
                    {
                        return outputWorkerVersionPath; // latest version is the default
                    }

                    if (compatibleWorkerCount > 1)
                    {
                        return languageWorkerVersionPath;
                    }
                }
            }

            return outputWorkerVersionPath;
        }

        /// <summary>
        /// Resolves worker configuration paths from the fallback directory within the host.
        /// </summary>
        private void ResolveWorkerConfigsFromWithinHost(string workerRuntime, Dictionary<string, string> outputDict)
        {
            var fallbackPath = _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath;

            _logger.LogDebug("Searching for worker configs in the fallback directory: {fallbackPath}", fallbackPath);

            foreach (var workerPath in _fileSystem.Directory.EnumerateDirectories(fallbackPath))
            {
                string workerDir = Path.GetFileName(workerPath);

                if (outputDict.ContainsKey(workerDir) || ShouldSkipWorkerDirectory(workerRuntime, workerDir))
                {
                    continue;
                }

                string workerConfigPath = Path.Combine(workerPath, RpcWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    outputDict[workerDir] = workerPath;
                }

                if (FoundWorkerConfigPath(workerRuntime, outputDict))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Returns a sorted list of worker version directories in descending order.
        /// </summary>
        private SortedList<Version, string> GetWorkerVersionsDescending(IEnumerable<string> workerVersionPaths)
        {
            // Map of: (parsed worker version, worker path)
            // Example: [ (1.0.0, "<rootProbingPath>/java/1.0.0"), (2.0.0, "<rootProbingPath>/java/2.0.0") ]
            var versionPathMap = new SortedList<Version, string>(new DescendingVersionComparer());

            foreach (var workerVersionPath in workerVersionPaths)
            {
                string versionDir = Path.GetFileName(workerVersionPath);

                if (Version.TryParse(versionDir, out Version version))
                {
                    versionPathMap[version] = workerVersionPath;
                }
                else
                {
                    _logger.LogDebug("Failed to parse worker version '{versionDir}' as a valid version.", versionDir);
                }
            }

            return versionPathMap;
        }

        /// <summary>
        /// Determines if the worker is compatible with Host by checking if Host satisfies worker requirements and by evaluating the profile conditions.
        /// </summary>
        private bool IsWorkerCompatibleWithHost(string workerDirPath)
        {
            string workerConfigPath = Path.Combine(workerDirPath, RpcWorkerConstants.WorkerConfigFileName);
            if (!File.Exists(workerConfigPath))
            {
                return false;
            }

            JsonElement workerConfigJson = WorkerConfigurationHelper.GetWorkerConfigJsonElement(workerConfigPath);

            if (workerConfigJson.ValueKind == JsonValueKind.Undefined)
            {
                return false;
            }

            // static capability resolution
            if (!DoesHostHasRequiredCapabilities(workerConfigJson, workerConfigPath))
            {
                return false;
            }

            // profiles evaluation
            RpcWorkerDescription workerDescription = WorkerConfigurationHelper.GetWorkerDescription(
                                                            workerConfig: workerConfigJson,
                                                            jsonSerializerOptions: _jsonSerializerOptions,
                                                            workerDir: workerDirPath,
                                                            profileManager: _profileManager,
                                                            languageWorkersSettings: _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
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
            var hostRequirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (workerConfig.TryGetProperty(RpcWorkerConstants.HostRequirementsSectionName, out JsonElement configSection))
            {
                foreach (var requirement in configSection.EnumerateArray())
                {
                    string requirementName = requirement.GetString();

                    if (!string.IsNullOrWhiteSpace(requirementName))
                    {
                        hostRequirements.Add(requirementName);
                    }
                }
            }

            return hostRequirements;
        }

        /// <summary>
        /// Determines if the host has all required capabilities specified in the worker configuration.
        /// </summary>
        private bool DoesHostHasRequiredCapabilities(JsonElement workerConfig, string workerConfigPath)
        {
            var hostCapabilities = ScriptConstants.HostCapabilities;
            var hostRequirements = GetHostRequirementsFromWorker(workerConfig);

            _logger.LogDebug("Worker configuration at '{workerConfigPath}' specifies host requirements [{requirements}].", workerConfigPath, string.Join(", ", hostRequirements));

            return hostRequirements.IsSubsetOf(hostCapabilities);
        }

        /// <summary>
        /// Determines if the worker directory should be skipped based on the current worker runtime and environment settings.
        /// </summary>
        internal bool ShouldSkipWorkerDirectory(string workerRuntime, string workerDir)
        {
            return !_workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment &&
                    !_workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled &&
                    workerRuntime is not null &&
                    !workerRuntime.Equals(workerDir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines if the required worker config path is found.
        /// </summary>
        internal bool FoundWorkerConfigPath(string workerRuntime, Dictionary<string, string> outputDict)
        {
            return !_workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment &&
                    !_workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled &&
                    !string.IsNullOrWhiteSpace(workerRuntime) &&
                    outputDict.ContainsKey(workerRuntime);
        }

        private bool IsStandardOrExtendedChannel()
        {
            string releaseChannel = _workerConfigurationResolverOptions.CurrentValue.ReleaseChannel;
            return !string.IsNullOrWhiteSpace(releaseChannel) &&
                                                (releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper, StringComparison.OrdinalIgnoreCase) ||
                                                releaseChannel.Equals(ScriptConstants.ExtendedPlatformChannelNameUpper, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Comparer for sorting Version objects in descending order.
        /// </summary>
        private class DescendingVersionComparer : IComparer<Version>
        {
            public int Compare(Version version1, Version version2)
            {
                return version2.CompareTo(version1); // Inverted comparison for descending order
            }
        }
    }
}