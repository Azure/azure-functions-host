// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// This class resolves worker configurations dynamically based on the current environment and configuration settings.
    /// It searches for worker configs in specified probing paths, and returns a list of worker configurations.
    /// </summary>
    internal sealed partial class DynamicWorkerConfigurationProvider : WorkerConfigurationProviderBase
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<DynamicWorkerConfigurationProvider> _logger;

        public DynamicWorkerConfigurationProvider(ILogger<DynamicWorkerConfigurationProvider> logger,
                        IMetricsLogger metricsLogger,
                        IFileSystem fileSystem,
                        IWorkerProfileManager workerProfileManager,
                        ISystemRuntimeInformation systemRuntimeInformation,
                        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
                        : base(metricsLogger, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override ILogger Logger { get => _logger; }

        public override int Priority { get => 3; }

        public override void PopulateWorkerConfigs(Dictionary<string, RpcWorkerConfig> workerRuntimeConfigMap)
        {
            if (!WorkerResolverOptions.IsDynamicWorkerResolutionEnabled)
            {
                return;
            }

            ResolveWorkerConfigsFromProbingPaths(workerRuntimeConfigMap, WorkerResolverOptions.ProbingPaths);
        }

        /// <summary>
        /// Resolves worker configurations from the specified probing paths.
        /// </summary>
        private void ResolveWorkerConfigsFromProbingPaths(Dictionary<string, RpcWorkerConfig> workerRuntimeToConfigMap, IReadOnlyList<string> workerProbingPaths)
        {
            try
            {
                Log.WorkerProbingPaths(Logger, string.Join(", ", workerProbingPaths));
                string workerRuntime = WorkerResolverOptions.WorkerRuntime;

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
                        if (workerRuntimeToConfigMap.ContainsKey(workerRuntimeDir))
                        {
                            Log.DuplicateRuntimeDirectory(Logger, workerRuntimeDir, probingPath);
                            continue;
                        }

                        // Skip worker directories that don't match the current runtime or are not enabled via hosting config. Do not load all workers after the specialization is done and if it is not a multi-language runtime environment
                        if (!WorkerResolverOptions.WorkersAvailableForResolution.Contains(workerRuntimeDir) ||
                           ShouldSkipWorkerDirectory(workerRuntime, workerRuntimeDir, WorkerResolverOptions.IsMultiLanguageWorkerEnvironment, WorkerResolverOptions.IsPlaceholderModeEnabled))
                        {
                            continue;
                        }

                        // Search for worker config inside version directories within the language worker directory
                        var resolvedWorkerConfig = ResolveWorkerConfigFromVersionsDirs(workerRuntimePath, workerRuntimeDir);
                        if (resolvedWorkerConfig is not null)
                        {
                            workerRuntimeToConfigMap[workerRuntimeDir] = resolvedWorkerConfig;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Catching exceptions such as unauthorized access, IO exception, path too long, that can happen while searching for configs in probing paths.
                // Logging the exception and continuing to search worker configs in the fallback path.
                Logger.LogError(ex, "Failed to resolve worker configurations from probing paths.");
            }
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
                if (WorkerResolverOptions.IgnoredWorkerVersions.TryGetValue(languageWorkerFolder, out HashSet<Version> value) && value.Contains(versionPair.Key))
                {
                    Log.IgnoreWorkerVersion(Logger, languageWorkerFolder, versionPair.Key.ToString());
                    continue;
                }

                var languageWorkerVersionPath = versionPair.Value;

                (var workerDescription, var workerConfigJson) = GetWorkerDescriptionAndConfig(languageWorkerVersionPath, WorkerResolverOptions.WorkerDescriptionOverrides);
                if (workerDescription is null || IsWorkerDescriptionDisabled(workerDescription))
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

            return BuildWorkerConfig(WorkerResolverOptions, resolvedWorkerVersionPath, resolvedWorkerConfig, resolvedWorkerDescription, MetricsLogger, Logger, SystemRuntimeInformation);
        }

        /// <summary>
        /// Returns a sorted list of worker version directories in descending order.
        /// </summary>
        private SortedList<Version, string> GetWorkerVersionsDescending(string languageWorkerPath)
        {
            var workerVersionPaths = _fileSystem.Directory.EnumerateDirectories(languageWorkerPath);

            // Map of: (parsed worker version, worker path). Example: [ (2.0.0, "<rootProbingPath>/java/2.0.0"), (1.0.0, "<rootProbingPath>/java/1.0.0") ]
            var versionPathMap = new SortedList<Version, string>(DescendingVersionComparer.Instance);

            foreach (var workerVersionPath in workerVersionPaths)
            {
                var versionDir = Path.GetFileName(workerVersionPath);

                if (Version.TryParse(versionDir, out Version version))
                {
                    versionPathMap[version] = workerVersionPath;
                }
                else
                {
                    Log.InvalidVersion(Logger, versionDir);
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
                Log.HostRequirements(Logger, workerDirPath, hostRequirementsSection.ToString());

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
                Log.ProbingPathNotExists(Logger, probingPath);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines if the current release channel is either the standard or extended platform channel.
        /// </summary>
        private bool IsStandardOrExtendedChannel()
        {
            var releaseChannel = WorkerResolverOptions.ReleaseChannel;

            return !string.IsNullOrWhiteSpace(releaseChannel) &&
                    (releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper, StringComparison.OrdinalIgnoreCase) ||
                    releaseChannel.Equals(ScriptConstants.ExtendedPlatformChannelNameUpper, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Comparer for sorting Version objects in descending order.
        /// </summary>
        private class DescendingVersionComparer : IComparer<Version>
        {
            public static readonly DescendingVersionComparer Instance = new();

            public int Compare(Version version1, Version version2)
            {
                return version2.CompareTo(version1); // Inverted comparison for descending order
            }
        }

        private static partial class Log
        {
            [LoggerMessage(LogLevel.Debug, "Worker probing paths set to: {workerProbingPaths}")]
            public static partial void WorkerProbingPaths(
                    ILogger logger,
                    string workerProbingPaths);

            [LoggerMessage(LogLevel.Debug, "Skipping duplicate worker runtime directory '{workerRuntimeDir}' in probing path '{probingPath}'.")]
            public static partial void DuplicateRuntimeDirectory(
                    ILogger logger,
                    string workerRuntimeDir,
                    string probingPath);

            [LoggerMessage(LogLevel.Debug, "Ignoring {languageWorkerFolder} version {version} as per configuration.")]
            public static partial void IgnoreWorkerVersion(
                    ILogger logger,
                    string languageWorkerFolder,
                    string version);

            [LoggerMessage(LogLevel.Debug, "Failed to parse worker version '{versionDir}' as a valid version.")]
            public static partial void InvalidVersion(
                    ILogger logger,
                    string versionDir);

            [LoggerMessage(LogLevel.Debug, "Worker configuration at '{workerDirPath}' specifies host requirements {requirements}.")]
            public static partial void HostRequirements(
                    ILogger logger,
                    string workerDirPath,
                    string requirements);

            [LoggerMessage(LogLevel.Debug, "Worker probing path directory does not exist: {probingPath}.")]
            public static partial void ProbingPathNotExists(
                    ILogger logger,
                    string probingPath);
        }
    }
}
