// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class LanguageWorkerOptionsSetup : IConfigureOptions<LanguageWorkerOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerProfileManager _workerProfileManager;
        private readonly IScriptHostManager _scriptHostManager;

        public LanguageWorkerOptionsSetup(IConfiguration configuration,
                                          ILoggerFactory loggerFactory,
                                          IEnvironment environment,
                                          IMetricsLogger metricsLogger,
                                          IWorkerProfileManager workerProfileManager,
                                          IScriptHostManager scriptHostManager)
        {
            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _workerProfileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));

            _logger = loggerFactory.CreateLogger("Host.LanguageWorkerConfig");
        }

        public void Configure(LanguageWorkerOptions options)
        {
            string workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);

            // Parsing worker.config.json should always be done in case of multi language worker
            if (!string.IsNullOrEmpty(workerRuntime) &&
                workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase) &&
                !_environment.IsMultiLanguageRuntimeEnvironment())
            {
                // Skip parsing worker.config.json files for dotnet in-proc apps
                options.WorkerConfigs = new List<RpcWorkerConfig>();
                return;
            }

            // Use the latest configuration from the ScriptHostManager if available.
            // After specialization, the ScriptHostManager will have the latest IConfiguration reflecting additional configuration entries added during specialization.
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

            var configFactory = new RpcWorkerConfigFactory(configuration, _logger, SystemRuntimeInformation.Instance, _environment, _metricsLogger, _workerProfileManager);
            options.WorkerConfigs = configFactory.GetConfigs();
        }
    }

    /// <summary>
    /// This implementation of IPostConfigureOptions validates that LanguageWorkerOptions are not configured within the JobHost scope.
    /// LanguageWorkerOptions should be forwarded from the parent scope.
    /// Triggers a debug failure and logs a message if unexpected configuration is detected.
    /// </summary>
    internal class JobHostLanguageWorkerOptionsSetup : IPostConfigureOptions<LanguageWorkerOptions>
    {
        private readonly ILoggerFactory _loggerFactory;

        public JobHostLanguageWorkerOptionsSetup(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public void PostConfigure(string name, LanguageWorkerOptions options)
        {
            var message = "Unexpected configuration of LanguageWorkerOptions from the JobHost scope. LanguageWorkerOptions should be forwarded from the parent scope with no additional configuration.";
            Debug.Fail(message);

            var logger = _loggerFactory.CreateLogger<JobHostLanguageWorkerOptionsSetup>();
            logger.LogInformation(message);
        }
    }
}
