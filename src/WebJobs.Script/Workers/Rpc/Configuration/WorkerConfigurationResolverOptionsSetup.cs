// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolverOptionsSetup : IConfigureOptions<WorkerConfigurationResolverOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        public WorkerConfigurationResolverOptionsSetup(ILoggerFactory loggerFactory, IConfiguration configuration, IScriptHostManager scriptHostManager, IFileSystem fileSystem)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public void Configure(WorkerConfigurationResolverOptions options)
        {
            var configuration = GetRequiredConfiguration();
            options.WorkersRootDirPath = GetWorkersRootDirPath(configuration);
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
    }
}
