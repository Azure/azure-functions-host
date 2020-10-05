// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class LanguageWorkerOptionsSetup : IConfigureOptions<LanguageWorkerOptions>
    {
        private IConfiguration _configuration;
        private ILogger _logger;
        private IEnvironment _environment;
        private IMetricsLogger _metricsLogger;

        public LanguageWorkerOptionsSetup(IConfiguration configuration, ILoggerFactory loggerFactory, IEnvironment environment, IMetricsLogger metricsLogger)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger("Host.LanguageWorkerConfig");
            _environment = environment;
            _metricsLogger = metricsLogger;
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
            ISystemRuntimeInformation systemRuntimeInfo = new SystemRuntimeInformation();
            var configFactory = new RpcWorkerConfigFactory(_configuration, _logger, systemRuntimeInfo, _environment, _metricsLogger);
            options.WorkerConfigs = configFactory.GetConfigs();
        }
    }
}
