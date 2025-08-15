// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolverOptionsSetup : IConfigureOptions<WorkerConfigurationResolverOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly IFileSystem _fileSystem;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;
        private readonly ILogger _logger;

        public WorkerConfigurationResolverOptionsSetup(ILoggerFactory loggerFactory,
                                                        IConfiguration configuration,
                                                        IEnvironment environment,
                                                        IFileSystem fileSystem,
                                                        IScriptHostManager scriptHostManager,
                                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _functionsHostingConfigOptions = functionsHostingConfigOptions ?? throw new ArgumentNullException(nameof(functionsHostingConfigOptions));
        }

        public void Configure(WorkerConfigurationResolverOptions options)
        {
            var configuration = _configuration;
            if (_scriptHostManager is IServiceProvider scriptHostManagerServiceProvider)
            {
                var latestConfiguration = scriptHostManagerServiceProvider.GetService<IConfiguration>();
                if (latestConfiguration is not null)
                {
                    configuration = new ConfigurationBuilder()
                        .AddConfiguration(_configuration)
                        .AddConfiguration(latestConfiguration)
                        .Build();
                }
            }

//            var configuration = GetRequiredConfiguration();
            options.WorkersRootDirPath = GetWorkersRootDirPath(configuration);
            options.WorkerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
            options.ReleaseChannel = EnvironmentExtensions.GetPlatformReleaseChannel(_environment);
            options.IsPlaceholderModeEnabled = _environment.IsPlaceholderModeEnabled();
            options.IsMultiLanguageWorkerEnvironment = _environment.IsMultiLanguageRuntimeEnvironment();
            options.WorkersDirPath = WorkerConfigurationHelper.GetWorkersDirPath(configuration);
            options.ProbingPaths = GetWorkerProbingPaths();
            options.WorkersAvailableForResolution = GetWorkersAvailableForResolutionViaHostingConfig(_functionsHostingConfigOptions);
            options.LanguageWorkersSettings = GetLanguageWorkersSettings(configuration);
        }

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

        private IConfiguration GetRequiredConfiguration()
        {
            string requiredSection = $"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}";

            if (_scriptHostManager is IServiceProvider scriptHostManagerServiceProvider)
            {
                var latestConfiguration = scriptHostManagerServiceProvider.GetService<IConfiguration>();
                var latestConfigValue = GetConfigurationSectionValue(latestConfiguration, nameof(latestConfiguration), requiredSection);

                if (!string.IsNullOrEmpty(latestConfigValue))
                {
                    return latestConfiguration;
                }
            }

            string configSectionValue = GetConfigurationSectionValue(_configuration, nameof(_configuration), requiredSection);
            if (!string.IsNullOrEmpty(configSectionValue))
            {
                return _configuration;
            }

            return null;
        }

        private string GetConfigurationSectionValue(IConfiguration configuration, string configurationSource, string requiredSection)
        {
            var section = configuration?.GetSection(requiredSection);

            if (!string.IsNullOrEmpty(section?.Value))
            {
                _logger.LogTrace("Found configuration section '{requiredSection}' in '{configurationSource}'.", requiredSection, configurationSource);
                return section.Value;
            }

            return null;
        }

        internal List<string> GetWorkerProbingPaths()
        {
            // If Configuration section is set, read probing paths from configuration.
            IConfigurationSection probingPathsSection = _configuration.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}")?.GetSection($"{RpcWorkerConstants.WorkerProbingPathsSectionName}");

            var probingPathsList = probingPathsSection?.AsEnumerable();

            List<string> probingPaths = new List<string>();

            if (probingPathsList is not null)
            {
                for (int i = 0; i < probingPathsList.Count(); i++)
                {
                    var path = probingPathsSection.GetSection($"{i}").Value;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        probingPaths.Add(path);
                    }
                }
            }
            else
            {
                if (_environment.IsHostedWindowsEnvironment())
                {
                    // Harcoded site extensions path for Windows until Antares sets it as an Environment variable.
                    // Example probing path for Windows: "c:\\home\\SiteExtensions\\workers"
                    string windowsSiteExtensionsPath = GetWindowsSiteExtensionsPath();

                    if (!string.IsNullOrWhiteSpace(windowsSiteExtensionsPath))
                    {
                        var windowsWorkerFullProbingPath = Path.Combine(windowsSiteExtensionsPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
                        probingPaths.Add(windowsWorkerFullProbingPath);
                    }
                }
            }

            return probingPaths;
        }

        internal static HashSet<string> GetWorkersAvailableForResolutionViaHostingConfig(IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions) =>
            (functionsHostingConfigOptions.Value?.WorkersAvailableForDynamicResolution ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        internal static string GetWindowsSiteExtensionsPath()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyPath);

            //Move 2 directories up to get to the SiteExtensions directory
            return Directory.GetParent(assemblyDir)?.Parent?.FullName;
        }

        internal Dictionary<string, string> GetLanguageWorkersSettings(IConfiguration configuration)
        {
            // Convert the required configuration sections to Dictionary
            var languageWorkersSettings = new Dictionary<string, string>();

            foreach (var kvp in configuration.AsEnumerable())
            {
                if (kvp.Key.StartsWith(RpcWorkerConstants.LanguageWorkersSectionName))
                {
                    languageWorkersSettings[kvp.Key] = kvp.Value;
                }
            }

            return languageWorkersSettings;
        }
    }
}
