// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class LanguageWorkerOptionsSetup : IConfigureOptions<LanguageWorkerOptions>
    {
        private readonly IEnvironment _environment;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerConfigurationResolver _workerConfigurationResolver;

        public LanguageWorkerOptionsSetup(IEnvironment environment,
                                          IMetricsLogger metricsLogger,
                                          IWorkerConfigurationResolver workerConfigurationResolver)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _workerConfigurationResolver = workerConfigurationResolver ?? throw new ArgumentNullException(nameof(workerConfigurationResolver));
        }

        public void Configure(LanguageWorkerOptions options)
        {
            string workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

            // Parsing worker.config.json should always be done in case of multi language worker
            if (!string.IsNullOrEmpty(workerRuntime) &&
                workerRuntime.Equals(RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase) &&
                !_environment.IsMultiLanguageRuntimeEnvironment())
            {
                // Skip parsing worker.config.json files for dotnet in-proc apps
                options.WorkerConfigs = new List<RpcWorkerConfig>();
                return;
            }

            using (_metricsLogger.LatencyEvent(MetricEventNames.GetConfigs))
            {
                var workerDescriptionDictionary = _workerConfigurationResolver.GetWorkerConfigs();
                options.WorkerConfigs = workerDescriptionDictionary.Values.ToList();
            }
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
