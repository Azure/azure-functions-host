// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
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
    /// It searches for worker configs in specified probing paths and the fallback path, and returns a list of worker configuration paths.
    /// </summary>
    internal sealed class DynamicWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IFileSystem _fileSystem;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;

        public DynamicWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                    IMetricsLogger metricsLogger,
                                                    IFileSystem fileSystem,
                                                    IWorkerProfileManager workerProfileManager,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _workerConfigurationResolverOptions = workerConfigResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigResolverOptions));
            ArgumentNullException.ThrowIfNull(workerConfigResolverOptions.CurrentValue);
        }

        public WorkerConfigurationInfo GetConfigurationInfo()
        {
            return new WorkerConfigurationInfo(
                WorkersRootDirPath: _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                WorkerConfigPaths: GetWorkerConfigPaths(),
                LanguageWorkersSettings: _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                CoreCount: _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
                FWRSetting: _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                FunctionsWorkerProcessCountSettingName: _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                WorkerRuntime: _workerConfigurationResolverOptions.CurrentValue.WorkerRuntime,
                Placeholder: _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                Multilanfg: _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment);
        }

        /// <summary>
        /// Gets the list of worker configuration paths by searching probing paths and fallback path.
        /// </summary>
        internal Dictionary<string, RpcWorkerConfig> GetWorkerConfigPaths()
        {
            var workerRuntime = _workerConfigurationResolverOptions.CurrentValue.WorkerRuntime;
            var workerProbingPaths = _workerConfigurationResolverOptions.CurrentValue.ProbingPaths;

            // Search for worker configs in probing paths. Returns a dictionary of { FUNCTIONS_WORKER_RUNTIME environment variable value : path of workerConfig }
            // Sample runtimeToConfigPathMap: {"java": "path1", "node": "path2", "dotnet-isolated": "path3"} for multilanguage worker scenario
            // Path format: "<rootProbingPath>/<workerRuntimeDir>/<workerVersion>/". Path example: "c:\\home\\SiteExtensions\\functionsworkers\\java\\1.0.0"
            var runtimeToConfigPathMap = ResolveWorkerConfigsFromProbingPaths(workerProbingPaths, workerRuntime);

            if (WorkerConfigurationHelper.FoundWorkerConfigPath(workerRuntime, runtimeToConfigPathMap, _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled, _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment))
            {
                return runtimeToConfigPathMap;
            }

            // Search in fallback path if worker cannot be found in probing paths
            runtimeToConfigPathMap = ResolveWorkerConfigsFromWithinHost(workerRuntime, runtimeToConfigPathMap);

            return runtimeToConfigPathMap;
        }

        /// <summary>
        /// Resolves worker configuration paths from the specified probing paths.
        /// </summary>
        private Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromProbingPaths(IReadOnlyList<string> workerProbingPaths, string workerRuntime)
        {
            var runtimeToConfigPathMap = new Dictionary<string, RpcWorkerConfig>(StringComparer.OrdinalIgnoreCase);

            try
            {
                _logger.WorkerProbingPaths(string.Join(", ", workerProbingPaths));

                // Probing path directory structure is: <probingPath>/<workerRuntimeDir>/<workerVersion>/<worker.config.json>
                foreach (var probingPath in workerProbingPaths)
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
                        if (runtimeToConfigPathMap.ContainsKey(workerRuntimeDir))
                        {
                            _logger.LogDebug("Skipping duplicate worker runtime directory '{workerRuntimeDir}' in probing path '{probingPath}'.", workerRuntimeDir, probingPath);
                            continue;
                        }

                        // Skip worker directories that don't match the current runtime or are not enabled via hosting config
                        // Do not load all workers after the specialization is done and if it is not a multi-language runtime environment
                        if (!_workerConfigurationResolverOptions.CurrentValue.WorkersAvailableForResolution.Contains(workerRuntimeDir) ||
                            WorkerConfigurationHelper.ShouldSkipWorkerDirectory(workerRuntime, workerRuntimeDir, _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled, _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment))
                        {
                            continue;
                        }

                        // Search for worker config inside version directories within the language worker directory
                        // Example workerVersionPath: "<rootProbingPath>/java/1.0.0"
                        var workerVersionPath = ResolveWorkerConfigFromVersionsDirs(workerRuntimePath, workerRuntimeDir);
                        if (workerVersionPath is not null)
                        {
                            runtimeToConfigPathMap[workerRuntimeDir] = workerVersionPath;
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

            return runtimeToConfigPathMap;
        }

        /// <summary>
        /// Resolves worker configuration paths from the version directories within a language worker directory.
        /// </summary>
        private RpcWorkerConfig ResolveWorkerConfigFromVersionsDirs(string languageWorkerPath, string languageWorkerFolder)
        {
            var workerVersionPaths = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);

            // Map of: (parsed worker version, worker path)
            // Example: [ (1.0.0, "<rootProbingPath>/java/1.0.0"), (2.0.0, "<rootProbingPath>/java/2.0.0") ]
            var versionPathMap = GetWorkerVersionsDescending(workerVersionPaths);

            int compatibleWorkerCount = 0;
            string outputWorkerVersionPath = null;
            var ignoredVersions = _workerConfigurationResolverOptions.CurrentValue.IgnoredWorkerVersions;
            bool standardOrExtendedChannel = IsStandardOrExtendedChannel();

            foreach (var versionPair in versionPathMap)
            {
                if (ignoredVersions.TryGetValue(languageWorkerFolder, out HashSet<Version> value) && value.Contains(versionPair.Key))
                {
                    _logger.LogDebug("Ignoring {languageWorkerFolder} version {version} as per configuration.", languageWorkerFolder, versionPair.Key);
                    continue;
                }

                string languageWorkerVersionPath = versionPair.Value;

                if (IsWorkerCompatibleWithHost(languageWorkerVersionPath))
                {
                    compatibleWorkerCount++;
                    outputWorkerVersionPath = languageWorkerVersionPath;

                    if (!standardOrExtendedChannel)
                    {
                        break; // latest version is the default
//                        return outputWorkerVersionPath; // latest version is the default
                    }

                    if (compatibleWorkerCount > 1)
                    {
                        outputWorkerVersionPath = languageWorkerVersionPath;
                        break;
                    }
                }
            }

            return WorkerConfigurationHelper.AddProvider(outputWorkerVersionPath,
                                                                    _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                                                                    _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                                                                    _metricsLogger,
                                                                    languageWorkerFolder,
                                                                    _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                                                                    _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                                                                    _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                                                                    _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment,
                                                                    _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
                                                                    _logger,
                                                                    SystemRuntimeInformation.Instance,
                                                                    _profileManager);
        }

        /// <summary>
        /// Resolves worker configuration paths from the fallback directory within the host.
        /// </summary>
        private Dictionary<string, RpcWorkerConfig> ResolveWorkerConfigsFromWithinHost(string workerRuntime, Dictionary<string, RpcWorkerConfig> runtimeToConfigPathMap)
        {
            var config = DefaultWorkerConfigurationResolver.ResolveWorkerConfigsFromWithinHost(_workerConfigurationResolverOptions.CurrentValue.WorkerRuntime,
                                                runtimeToConfigPathMap,
                                                _workerConfigurationResolverOptions.CurrentValue.WorkersRootDirPath,
                                                _workerConfigurationResolverOptions.CurrentValue.LanguageWorkersSettings,
                                                _logger,
                                                _fileSystem,
                                                _metricsLogger,
                                                _workerConfigurationResolverOptions.CurrentValue.FunctionWorkerRuntimeVersionSettingName,
                                                _workerConfigurationResolverOptions.CurrentValue.FunctionsWorkerProcessCountSettingName,
                                                _workerConfigurationResolverOptions.CurrentValue.IsPlaceholderModeEnabled,
                                                _workerConfigurationResolverOptions.CurrentValue.IsMultiLanguageWorkerEnvironment,
                                                _workerConfigurationResolverOptions.CurrentValue.EffectiveCoresCount,
                                                SystemRuntimeInformation.Instance,
                                                _profileManager);

            return runtimeToConfigPathMap;
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

            var workerConfigJson = WorkerConfigurationHelper.GetWorkerConfigJsonElement(workerConfigPath);

            if (workerConfigJson.ValueKind == JsonValueKind.Undefined)
            {
                _logger.LogDebug("Skipping worker at '{workerConfigPath}' due to undefined JsonElement.", workerConfigPath);
                return false;
            }

            // static capability resolution
            if (workerConfigJson.TryGetProperty(RpcWorkerConstants.HostRequirementsSectionName, out JsonElement configSection))
            {
                var hostRequirements = configSection.Deserialize<HashSet<string>>(JsonSerializerOptionsProvider.WorkerConfigJsonSerializerOptions);
                if (!HostHasRequiredCapabilities(hostRequirements, workerConfigPath))
                {
                    return false;
                }
            }

            // profiles evaluation
            RpcWorkerDescription workerDescription = WorkerConfigurationHelper.GetWorkerDescription(
                                                            workerConfig: workerConfigJson,
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
        /// Determines if the host has all required capabilities specified in the worker configuration.
        /// </summary>
        private bool HostHasRequiredCapabilities(HashSet<string> hostRequirements, string workerConfigPath)
        {
            _logger.LogDebug("Worker configuration at '{workerConfigPath}' specifies host requirements [{requirements}].", workerConfigPath, string.Join(", ", hostRequirements));

            if (hostRequirements is null || hostRequirements.Count == 0)
            {
                return true;
            }

            var hostCapabilities = ScriptConstants.HostCapabilities;

            return hostRequirements.IsSubsetOf(hostCapabilities);
        }

        /// <summary>
        /// Determines if the current release channel is either the standard or extended platform channel.
        /// </summary>
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