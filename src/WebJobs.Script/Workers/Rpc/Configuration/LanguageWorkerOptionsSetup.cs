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
        private readonly IServiceProvider _serviceProvider;
        private readonly IScriptHostManager _scriptHostManager;

        public LanguageWorkerOptionsSetup(IConfiguration configuration,
                                          ILoggerFactory loggerFactory,
                                          IEnvironment environment,
                                          IMetricsLogger metricsLogger,
                                          IWorkerProfileManager workerProfileManager,
                                          IScriptHostManager scriptHostManager
            //,  IServiceProvider serviceProvider
            )
        {
            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
            // _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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

            //            var latestConfiguration = _serviceProvider.GetRequiredService<IConfiguration>();

            if (_scriptHostManager is IServiceProvider scriptHostManagerServiceProvider)
            {
                var config = scriptHostManagerServiceProvider.GetService<IConfiguration>();
                if (config is not null)
                {
                    // to do: rebuild by merging this config and other IConfiguration instance.
                }
            }
            var configFactory = new RpcWorkerConfigFactory(_configuration, _logger, SystemRuntimeInformation.Instance, _environment, _metricsLogger, _workerProfileManager);
            options.WorkerConfigs = configFactory.GetConfigs();
        }
    }

    internal class JobHostLanguageWorkerOptionsSetup : IPostConfigureOptions<LanguageWorkerOptions>
    {
        private readonly ILoggerFactory _loggerFactory;

        public JobHostLanguageWorkerOptionsSetup(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public void PostConfigure(string name, LanguageWorkerOptions options)
        {
            var message = $"Call to configure {nameof(LanguageWorkerOptions)} from the JobHost scope. " +
                $"If using {nameof(IOptions<LanguageWorkerOptions>)}, please use {nameof(IOptionsMonitor<LanguageWorkerOptions>)} instead.";
            Debug.Fail(message);

            var logger = _loggerFactory.CreateLogger("Host.LanguageWorkerConfig");
            logger.LogInformation(message);
        }
    }
}
