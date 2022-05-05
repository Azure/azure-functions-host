// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class LanguageWorkerOptionsSetup : IConfigureOptions<LanguageWorkerOptions>
    {
        private readonly IWorkerProfileManager _profileConditionManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IMetricsLogger _metricsLogger;

        public LanguageWorkerOptionsSetup(IConfiguration configuration,
                                          ILoggerFactory loggerFactory,
                                          IEnvironment environment,
                                          IWorkerProfileManager profileConditionManager,
                                          IMetricsLogger metricsLogger)
        {
            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _profileConditionManager = profileConditionManager ?? throw new System.ArgumentNullException(nameof(profileConditionManager));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));

            _logger = loggerFactory.CreateLogger("Host.LanguageWorkerConfig");
        }

        public void Configure(LanguageWorkerOptions options)
        {
            string workerRuntime = _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (!string.IsNullOrEmpty(workerRuntime) && workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, System.StringComparison.OrdinalIgnoreCase))
            {
                // Skip parsing worker.config.json files for dotnet in-proc apps
                options.WorkerConfigs = new List<RpcWorkerConfig>();
                return;
            }

            var configFactory = new RpcWorkerConfigFactory(_configuration, _logger, SystemRuntimeInformation.Instance,
                _profileConditionManager, _environment, _metricsLogger);

            options.WorkerConfigs = configFactory.GetConfigs();
        }
    }
}
