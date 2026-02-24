// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolverOptionsSetup : IConfigureOptions<WorkerConfigurationResolverOptions>
    {
        private const string WebHostConfigurationSource = "WebHost";
        private const string JobHostConfigurationSource = "JobHost";
        private readonly IConfiguration _configuration;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IEnvironment _environment;
        private readonly IFileSystem _fileSystem;
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;
        private readonly ILogger _logger;
        private readonly IWorkerRuntimeResolver _workerRuntimeResolver;

        public WorkerConfigurationResolverOptionsSetup(ILoggerFactory loggerFactory,
                                                        IConfiguration configuration,
                                                        IEnvironment environment,
                                                        IFileSystem fileSystem,
                                                        IScriptHostManager scriptHostManager,
                                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions,
                                                        IWorkerRuntimeResolver workerRuntimeResolver)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _functionsHostingConfigOptions = functionsHostingConfigOptions ?? throw new ArgumentNullException(nameof(functionsHostingConfigOptions));
            ArgumentNullException.ThrowIfNull(_functionsHostingConfigOptions.Value);
            _workerRuntimeResolver = workerRuntimeResolver ?? throw new ArgumentNullException(nameof(workerRuntimeResolver));
        }

        public void Configure(WorkerConfigurationResolverOptions options)
        {
            var configuration = GetRequiredConfiguration();
            options.WorkersRootDirPath = GetWorkersRootDirPath(configuration);
            options.WorkerRuntime = _workerRuntimeResolver.GetWorkerRuntime();
            options.WorkersAvailableForResolution = GetWorkersAvailableForResolution();
            options.IsPlaceholderModeEnabled = _environment.IsPlaceholderModeEnabled();
            options.IsMultiLanguageWorkerEnvironment = _environment.IsMultiLanguageRuntimeEnvironment();
            options.WorkerDescriptionOverrides = GetWorkerDescriptionOverrides(configuration);
            options.WorkerProcessCount = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName);
            options.FunctionsWorkerRuntimeVersion = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName);
            options.EffectiveCoresCount = _environment.GetEffectiveCoresCount();
            options.IsDynamicWorkerResolutionEnabled = IsDynamicWorkerResolutionEnabled(options.WorkerRuntime, options.WorkersAvailableForResolution, options.IsPlaceholderModeEnabled, options.IsMultiLanguageWorkerEnvironment);

            if (options.IsDynamicWorkerResolutionEnabled)
            {
                options.ReleaseChannel = _environment.GetPlatformReleaseChannel();
                options.ProbingPaths = GetWorkerProbingPaths(configuration);
                options.IgnoredWorkerVersions = GetIgnoredWorkerVersions();
            }
        }

        /// <summary>
        /// Returns the default workers directory path within the Host based on the current assembly location.
        /// </summary>
        internal string GetDefaultWorkersDirectory()
        {
            var assemblyDir = AppContext.BaseDirectory;
            string workersDirPath = Path.Combine(assemblyDir, RpcWorkerConstants.DefaultWorkersDirectoryName);

            if (!_fileSystem.Directory.Exists(workersDirPath))
            {
                // Site Extension Path. Default to parent directory.
                var parentDir = _fileSystem.Directory.GetParent(assemblyDir.TrimEnd(Path.DirectorySeparatorChar)).FullName;
                workersDirPath = _fileSystem.Path.Combine(parentDir, RpcWorkerConstants.DefaultWorkersDirectoryName);
            }
            return workersDirPath;
        }

        /// <summary>
        /// Gets the workers root directory path from configuration, or returns the default workers directory path within the Host if not set.
        /// </summary>
        private string GetWorkersRootDirPath(IConfiguration configuration)
        {
            if (configuration is not null)
            {
                var workersDirectorySection = configuration.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}");

                if (!string.IsNullOrEmpty(workersDirectorySection?.Value))
                {
                    return workersDirectorySection.Value;
                }
            }

            return GetDefaultWorkersDirectory();
        }

        /// <summary>
        /// Combines the base configuration and the latest configuration from the script host manager, if available.
        /// </summary>
        private IConfiguration GetRequiredConfiguration()
        {
            LogWorkersDirSectionPresence(_configuration, WebHostConfigurationSource);
            var configuration = _configuration;

            // Use the latest configuration from the ScriptHostManager if available.
            // After specialization, the ScriptHostManager will have the latest IConfiguration reflecting additional configuration entries added during specialization.
            if (_scriptHostManager is IServiceProvider scriptHostManagerServiceProvider)
            {
                var latestConfiguration = scriptHostManagerServiceProvider.GetService<IConfiguration>();

                if (latestConfiguration is not null)
                {
                    LogWorkersDirSectionPresence(latestConfiguration, JobHostConfigurationSource);

                    configuration = new ConfigurationBuilder()
                        .AddConfiguration(_configuration)
                        .AddConfiguration(latestConfiguration)
                        .Build();
                }
            }

            return configuration;
        }

        /// <summary>
        /// Logs a message if the required configuration section is found.
        /// </summary>
        private void LogWorkersDirSectionPresence(IConfiguration configuration, string configurationSource)
        {
            var configSectionToCheck = ConfigurationPath.Combine(RpcWorkerConstants.LanguageWorkersSectionName, WorkerConstants.WorkersDirectorySectionName);
            var section = configuration.GetSection(configSectionToCheck);

            if (!string.IsNullOrEmpty(section.Value))
            {
                _logger.LogDebug("Found configuration section '{requiredSection}' in {configurationSource}.", configSectionToCheck, configurationSource);
            }
        }

        /// <summary>
        /// Retrieves the list of worker probing paths from configuration or uses the default path for Windows environment.
        /// </summary>
        private List<string> GetWorkerProbingPaths(IConfiguration configuration)
        {
            // If Configuration section is set, read probing paths from configuration.
            var probingPathsSection = configuration.GetSection(ConfigurationPath.Combine(RpcWorkerConstants.LanguageWorkersSectionName, RpcWorkerConstants.WorkerProbingPathsSectionName));
            var probingPaths = probingPathsSection.Get<List<string>>();
            _logger.LogDebug("Worker probing paths specified via configuration: {probingPaths}.", probingPaths);

            probingPaths = probingPaths ?? [];

            if (probingPaths.Count == 0)
            {
                if (_environment.IsHostedWindowsEnvironment())
                {
                    // Default worker probing path for Windows
                    var windowsSiteExtensionsPath = GetWindowsSiteExtensionsPath();

                    if (!string.IsNullOrWhiteSpace(windowsSiteExtensionsPath))
                    {
                        // Example probing path for Windows: "c:\\home\\SiteExtensions\\functionsworkers"
                        var windowsWorkerProbingPath = Path.Combine(windowsSiteExtensionsPath, RpcWorkerConstants.FunctionsWorkersDirectoryName);
                        probingPaths.Add(windowsWorkerProbingPath);
                        _logger.LogDebug("Default worker probing path for Windows: {windowsWorkerProbingPath}.", windowsWorkerProbingPath);
                    }
                }
            }

            return probingPaths;
        }

        /// <summary>
        /// Returns the default site extensions path for Windows environment.
        /// </summary>
        private static string GetWindowsSiteExtensionsPath()
        {
            var assemblyDir = AppContext.BaseDirectory;

            //Move 2 directories up to get to the SiteExtensions directory. Example output: "c:\\home\\SiteExtensions"
            return Directory.GetParent(assemblyDir.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.FullName;
        }

        /// <summary>
        /// Returns a set of worker runtimes available for dynamic resolution from hosting config.
        /// </summary>
        private HashSet<string> GetWorkersAvailableForResolution() =>
            (_functionsHostingConfigOptions.Value.WorkersAvailableForDynamicResolution ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Converts language workers related configuration sections to a dictionary.
        /// Output format: { language: RpcWorkerDescription }.
        /// </summary>
        private static Dictionary<string, RpcWorkerDescription> GetWorkerDescriptionOverrides(IConfiguration configuration)
        {
            var workerDescriptionsMap = new Dictionary<string, RpcWorkerDescription>();
            var languageWorkersSection = configuration.GetSection(RpcWorkerConstants.LanguageWorkersSectionName);
            languageWorkersSection.Bind(workerDescriptionsMap);

            // special handling for Arguments which takes a string but internally requires a List<string>.
            for (int i = 0; i < workerDescriptionsMap.Keys.Count; i++)
            {
                var (language, workerDescription) = workerDescriptionsMap.ElementAt(i);
                var arguments = languageWorkersSection.GetSection(language).GetValue<string>(WorkerConstants.WorkerDescriptionArguments);
                if (!string.IsNullOrEmpty(arguments))
                {
                    workerDescription.Arguments = RegexHolder.WhiteSpaceRegex().Split(arguments);
                    workerDescriptionsMap[language] = workerDescription;
                }
            }

            return workerDescriptionsMap;
        }

        /// <summary>
        /// Gets a value indicating whether dynamic worker resolution is enabled.
        /// Users can disable dynamic worker resolution via setting the appropriate feature flag.
        /// Worker resolution can be enabled for specific workers at the stamp level via hosting config options.
        /// Feature flag takes precedence over hosting config options.
        /// </summary>
        private bool IsDynamicWorkerResolutionEnabled(string workerRuntime, IReadOnlySet<string> workersAvailableForResolution, bool isPlaceholderModeEnabled, bool isMultiLanguageEnv)
        {
            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableDynamicWorkerResolution, _environment) || workersAvailableForResolution.Count == 0)
            {
                return false;
            }

            if (!isPlaceholderModeEnabled && !isMultiLanguageEnv && !string.IsNullOrWhiteSpace(workerRuntime))
            {
                return workersAvailableForResolution.Contains(workerRuntime);
            }

            return true;
        }

        /// <summary>
        /// Returns a dictionary of worker names to sets of ignored versions, parsed from hosting config options.
        /// Output format: { worker-name: { hashset of versions to be ignored }}.
        /// Sample output: {"java": {"2.19.0", "2.18.0"}, "dotnet-isolated": {"1.0.0"}}.
        /// </summary>
        private Dictionary<string, HashSet<Version>> GetIgnoredWorkerVersions()
        {
            // Example value of ignoredWorkerVersions: "Worker1Name:Version1|Worker1Name:Version2|Worker2Name:Version1|Worker3Name:Version1".
            var ignoredWorkerVersions = _functionsHostingConfigOptions.Value.IgnoredWorkerVersions;

            if (string.IsNullOrWhiteSpace(ignoredWorkerVersions))
            {
                return new Dictionary<string, HashSet<Version>>();
            }

            var ignoredVersions = ignoredWorkerVersions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ignoredVersionsOut = new Dictionary<string, HashSet<Version>>(StringComparer.OrdinalIgnoreCase);

            foreach (string ignoredVersion in ignoredVersions)
            {
                string[] workerVersionParts = ignoredVersion.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (workerVersionParts.Length != 2)
                {
                    _logger.LogDebug("Skipping '{ignoredVersion}' due to invalid format for ignored worker version. Expected format is 'WorkerName:Version'.", ignoredVersion);
                    continue;
                }

                var workerName = workerVersionParts[0];
                var version = workerVersionParts[1];

                if (!Version.TryParse(version, out Version parsedVersion))
                {
                    _logger.LogDebug("Skipping '{ignoredVersion}' due to invalid version format: '{version}' for worker '{workerName}'.", ignoredVersion, version, workerName);
                    continue;
                }

                if (ignoredVersionsOut.TryGetValue(workerName, out HashSet<Version> value))
                {
                    value.Add(parsedVersion);
                    ignoredVersionsOut[workerName] = value;
                }
                else
                {
                    ignoredVersionsOut[workerName] = [parsedVersion];
                }
            }

            return ignoredVersionsOut;
        }
    }

    internal static partial class RegexHolder
    {
#if NET7_0_OR_GREATER
        [GeneratedRegex(@"\s+")]
        public static partial Regex WhiteSpaceRegex();
#else
        public static Regex WhiteSpaceRegex() => new Regex(@"\s+");
#endif
    }
}
