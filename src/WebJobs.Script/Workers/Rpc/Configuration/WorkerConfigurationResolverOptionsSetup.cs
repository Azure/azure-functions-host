// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolverOptionsSetup : IConfigureOptions<WorkerConfigurationResolverOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IScriptHostManager _scriptHostManager;

        public WorkerConfigurationResolverOptionsSetup(IConfiguration configuration, IScriptHostManager scriptHostManager)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
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

            options.WorkersDirPath = GetWorkersDirPath(configuration);
        }

        internal static string GetDefaultWorkersDirectory(Func<string, bool> directoryExists)
        {
            var assemblyDir = AppContext.BaseDirectory;
            string workersDirPath = Path.Combine(assemblyDir, RpcWorkerConstants.DefaultWorkersDirectoryName);

            if (!directoryExists(workersDirPath))
            {
                // Site Extension Path. Default to parent directory.
                var parentDir = Directory.GetParent(assemblyDir.TrimEnd(Path.DirectorySeparatorChar)).FullName;
                workersDirPath = Path.Combine(parentDir, RpcWorkerConstants.DefaultWorkersDirectoryName);
            }
            return workersDirPath;
        }

        internal static string GetWorkersDirPath(IConfiguration configuration)
        {
            string workersDirPath = GetDefaultWorkersDirectory(Directory.Exists);
            var workersDirectorySection = configuration?.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}");

            if (!string.IsNullOrEmpty(workersDirectorySection?.Value))
            {
                workersDirPath = workersDirectorySection.Value;
            }

            return workersDirPath;
        }
    }
}