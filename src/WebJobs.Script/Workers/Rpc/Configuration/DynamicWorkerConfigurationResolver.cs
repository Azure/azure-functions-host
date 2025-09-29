// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// This class resolves worker configurations dynamically based on the current environment and configuration settings.
    /// It searches for worker configs in specified probing paths and the fallback path, and returns a list of worker configurations.
    /// </summary>
    internal sealed class DynamicWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IFileSystem _fileSystem;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _resolverOptions;
        private readonly ISystemRuntimeInformation _systemRuntimeInformation;

        public DynamicWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                    IMetricsLogger metricsLogger,
                                                    IFileSystem fileSystem,
                                                    IWorkerProfileManager workerProfileManager,
                                                    ISystemRuntimeInformation systemRuntimeInformation,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _systemRuntimeInformation = systemRuntimeInformation ?? throw new ArgumentNullException(nameof(systemRuntimeInformation));
            _resolverOptions = workerConfigResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigResolverOptions));
            ArgumentNullException.ThrowIfNull(_resolverOptions.CurrentValue);
        }

        /// <summary>
        /// Retrieves a dictionary of worker configurations by searching the probing paths and fallback path.
        /// The returned dictionary maps FUNCTIONS_WORKER_RUNTIME values to the corresponding RpcWorkerConfig - { FUNCTIONS_WORKER_RUNTIME : RpcWorkerConfig }.
        /// </summary>
        public Dictionary<string, RpcWorkerConfig> GetWorkerConfigs()
        {
            var workerRuntime = _resolverOptions.CurrentValue.WorkerRuntime;
            var workerProbingPaths = _resolverOptions.CurrentValue.ProbingPaths;

            // Search for worker configs in probing paths
            var runtimeToConfigMap = ResolveWorkerConfigsFromProbingPaths(workerProbingPaths, workerRuntime);

            // Return if required worker config has been found
            if (!_resolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment && !_resolverOptions.CurrentValue.IsPlaceholderModeEnabled && !string.IsNullOrWhiteSpace(workerRuntime) && runtimeToConfigMap.ContainsKey(workerRuntime))
            {
                return runtimeToConfigMap;
            }

            // Search in fallback path if worker config cannot be found in probing paths
            return ResolveWorkerConfigsFromWithinHost(runtimeToConfigMap);
        }

        /// <summary>
        /// Resolves worker configurations from the specified probing paths.
        /// </summary>
        private Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromProbingPaths(IReadOnlyList<string> workerProbingPaths, string workerRuntime)
        {
            var runtimeToConfigMap = new Dictionary<string, RpcWorkerConfig>(StringComparer.OrdinalIgnoreCase);

            try
            {
                _logger.WorkerProbingPaths(string.Join(", ", workerProbingPaths));

                // Probing path directory structure is: "<rootPath>/<workerRuntimeDir>/<workerVersion>/worker.config.json"
                foreach (var probingPath in workerProbingPaths)
                {
                    if (!IsValidProbingPath(probingPath))
                    {
                        continue;
                    }

                    foreach (var workerRuntimePath in _fileSystem.Directory.EnumerateDirectories(probingPath))
                    {
                        var workerRuntimeDir = Path.GetFileName(workerRuntimePath);

                        // If probing paths are malformed and have duplicate directories of the same language worker (eg. due to different casing)
                        if (runtimeToConfigMap.ContainsKey(workerRuntimeDir))
                        {
                            _logger.LogDebug("Skipping duplicate worker runtime directory '{workerRuntimeDir}' in probing path '{probingPath}'.", workerRuntimeDir, probingPath);
                            continue;
                        }

                        // Skip worker directories that don't match the current runtime or are not enabled via hosting config. Do not load all workers after the specialization is done and if it is not a multi-language runtime environment
                        if (!_resolverOptions.CurrentValue.WorkersAvailableForResolution.Contains(workerRuntimeDir) ||
                           WorkerConfigurationHelper.ShouldSkipWorkerDirectory(workerRuntime, workerRuntimeDir, _resolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment, _resolverOptions.CurrentValue.IsPlaceholderModeEnabled))
                        {
                            continue;
                        }

                        // Search for worker config inside version directories within the language worker directory
                        var resolvedWorkerConfig = ResolveWorkerConfigFromVersionsDirs(workerRuntimePath, workerRuntimeDir);
                        if (resolvedWorkerConfig is not null)
                        {
                            runtimeToConfigMap[workerRuntimeDir] = resolvedWorkerConfig;
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

            return runtimeToConfigMap;
        }

        /// <summary>
        /// Resolves worker configuration from version directories within a language worker directory.
        /// </summary>
        private RpcWorkerConfig ResolveWorkerConfigFromVersionsDirs(string languageWorkerPath, string languageWorkerFolder)
        {
            var versionPathMap = GetWorkerVersionsDescending(languageWorkerPath);
            var standardOrExtendedChannel = IsStandardOrExtendedChannel();

            var compatibleWorkerCount = 0;
            (string resolvedWorkerVersionPath, JsonElement resolvedWorkerConfig, RpcWorkerDescription resolvedWorkerDescription) = (null, default, null);

            foreach (var versionPair in versionPathMap)
            {
                if (_resolverOptions.CurrentValue.IgnoredWorkerVersions.TryGetValue(languageWorkerFolder, out HashSet<Version> value) && value.Contains(versionPair.Key))
                {
                    _logger.LogDebug("Ignoring {languageWorkerFolder} version {version} as per configuration.", languageWorkerFolder, versionPair.Key);
                    continue;
                }

                var languageWorkerVersionPath = versionPair.Value;

                (var workerDescription, var workerConfigJson) = WorkerConfigurationHelper.GetWorkerDescriptionAndConfig(languageWorkerVersionPath, _profileManager, _resolverOptions.CurrentValue.WorkerDescriptionOverrides, _logger);
                if (workerDescription is null || WorkerConfigurationHelper.IsWorkerDescriptionDisabled(workerDescription, _logger))
                {
                    continue;
                }

                if (IsWorkerCompatibleWithHost(languageWorkerVersionPath, workerConfigJson))
                {
                    compatibleWorkerCount++;
                    (resolvedWorkerVersionPath, resolvedWorkerConfig, resolvedWorkerDescription) = (languageWorkerVersionPath, workerConfigJson, workerDescription);

                    // If it is standard or extended channel, look for the next compatible worker and break.
                    if (!standardOrExtendedChannel || compatibleWorkerCount > 1)
                    {
                        break;
                    }
                }
            }

            if (resolvedWorkerVersionPath is null)
            {
                return null;
            }

            return WorkerConfigurationHelper.BuildWorkerConfig(_resolverOptions.CurrentValue, resolvedWorkerVersionPath, resolvedWorkerConfig, resolvedWorkerDescription, _metricsLogger, _logger, _systemRuntimeInformation);
        }

        /// <summary>
        /// Resolves worker configurations from the fallback directory within the host.
        /// </summary>
        private Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromWithinHost(Dictionary<string, RpcWorkerConfig> availableRuntimeToConfigMap)
        {
            _logger.LogDebug("Searching for worker configs in the fallback directory: {fallbackPath}", _resolverOptions.CurrentValue.WorkersRootDirPath);

            return DefaultWorkerConfigurationResolver.ResolveWorkerConfigsFromWithinHost(_resolverOptions.CurrentValue,
                                                                                                _logger,
                                                                                                _fileSystem,
                                                                                                _metricsLogger,
                                                                                                _systemRuntimeInformation,
                                                                                                _profileManager,
                                                                                                availableRuntimeToConfigMap);
        }

        /// <summary>
        /// Returns a sorted list of worker version directories in descending order.
        /// </summary>
        private SortedList<Version, string> GetWorkerVersionsDescending(string languageWorkerPath)
        {
            var workerVersionPaths = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);

            // Map of: (parsed worker version, worker path). Example: [ (2.0.0, "<rootProbingPath>/java/2.0.0"), (1.0.0, "<rootProbingPath>/java/1.0.0") ]
            var versionPathMap = new SortedList<Version, string>(new DescendingVersionComparer());

            foreach (var workerVersionPath in workerVersionPaths)
            {
                var versionDir = Path.GetFileName(workerVersionPath);

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
        /// Determines if the worker is compatible with Host by checking if Host satisfies worker requirements.
        /// </summary>
        private bool IsWorkerCompatibleWithHost(string workerDirPath, JsonElement workerConfigJson)
        {
            if (workerConfigJson.TryGetProperty(RpcWorkerConstants.HostRequirementsSectionName, out JsonElement hostRequirementsSection))
            {
                _logger.LogDebug("Worker configuration at '{workerDirPath}' specifies host requirements {requirements}.", workerDirPath, hostRequirementsSection);

                var hostRequirements = hostRequirementsSection.Deserialize<HashSet<string>>(JsonSerializerOptionsProvider.CaseInsensitiveJsonSerializerOptions);

                if (hostRequirements is not null && !hostRequirements.IsSubsetOf(ScriptConstants.HostCapabilities))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the provided probing path is valid by ensuring it is not null and the directory exists in the file system.
        /// </summary>
        private bool IsValidProbingPath(string probingPath)
        {
            if (string.IsNullOrWhiteSpace(probingPath))
            {
                return false;
            }

            if (!_fileSystem.Directory.Exists(probingPath))
            {
                _logger.LogDebug("Worker probing path directory does not exist: {probingPath}.", probingPath);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines if the current release channel is either the standard or extended platform channel.
        /// </summary>
        private bool IsStandardOrExtendedChannel()
        {
            var releaseChannel = _resolverOptions.CurrentValue.ReleaseChannel;

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