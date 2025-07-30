// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolverOptionsSetup : IConfigureOptions<WorkerConfigurationResolverOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;

        public WorkerConfigurationResolverOptionsSetup(IConfiguration configuration,
                                                        IEnvironment environment,
                                                        IScriptHostManager scriptHostManager,
                                                        IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
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

            options.WorkersDirPath = GetWorkersDirPath(configuration);
        }

        internal static string GetDefaultWorkersDirectory(Func<string, bool> directoryExists)
        {
#pragma warning disable SYSLIB0012 // Type or member is obsolete
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.CodeBase).LocalPath);
#pragma warning restore SYSLIB0012 // Type or member is obsolete
            string workersDirPath = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            if (!directoryExists(workersDirPath))
            {
                // Site Extension. Default to parent directory
                workersDirPath = Path.Combine(Directory.GetParent(assemblyLocalPath).FullName, RpcWorkerConstants.DefaultWorkersDirectoryName);
            }
            return workersDirPath;
        }

        internal static string GetWorkersDirPath(IConfiguration configuration)
        {
            var workersDirectorySection = configuration?.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}");

            string workersDirPath = GetDefaultWorkersDirectory(Directory.Exists);

            if (!string.IsNullOrEmpty(workersDirectorySection?.Value))
            {
                workersDirPath = workersDirectorySection.Value;
            }

            return workersDirPath;
        }
    }
}