// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
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
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;

        public LanguageWorkerOptionsSetup(IConfiguration configuration,
                                          ILoggerFactory loggerFactory,
                                          IEnvironment environment,
                                          IMetricsLogger metricsLogger,
                                          IWorkerProfileManager workerProfileManager,
                                          IScriptHostManager scriptHostManager,
                                          IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
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
            _functionsHostingConfigOptions = functionsHostingConfigOptions ?? throw new ArgumentNullException(nameof(functionsHostingConfigOptions));

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

            string jsonString = GetWorkerProbingPaths();

            if (!string.IsNullOrEmpty(jsonString))
            {
                using var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonString));

                configuration = new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddJsonStream(jsonStream)
                    .Build();
            }

            var workerConfigurationResolver = new WorkerConfigurationResolver(configuration, _logger, _environment, _workerProfileManager, _functionsHostingConfigOptions);
            var configFactory = new RpcWorkerConfigFactory(configuration, _logger, SystemRuntimeInformation.Instance, _environment, _metricsLogger, _workerProfileManager, workerConfigurationResolver, _functionsHostingConfigOptions);
            options.WorkerConfigs = configFactory.GetConfigs();
        }

        public string GetWorkerProbingPaths()
        {
            var probingPaths = new List<string>();
            string output = string.Empty;

            // If Env variable is available, read from there (works for linux)
            var probingPathsEnvValue = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.WorkerProbingPaths, null);

            if (!string.IsNullOrEmpty(probingPathsEnvValue))
            {
                probingPaths = probingPathsEnvValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? new List<string>();
            }
            else
            {
                if (_environment.IsAnyWindows())
                {
                    // Harcoded site extensions path ("c:\\home\\SiteExtensions\\workers") for Windows until Antares starts setting this as Environment variable.
                    //  probingPaths.Add("c:\\testData\\workers");

#pragma warning disable SYSLIB0012 // Type or member is obsolete
                    string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(LanguageWorkerOptionsSetup).Assembly.CodeBase).LocalPath);
#pragma warning restore SYSLIB0012 // Type or member is obsolete
                    string workersDirPath = Path.Combine(assemblyLocalPath, "ProbingPaths");
                    probingPaths.Add(workersDirPath);
                    _logger.LogInformation($"ProbingPaths setup via options: {workersDirPath}");
                }
            }

            if (probingPaths.Any())
            {
                var jsonObj = new
                {
                    languageWorkers = new
                    {
                        probingPaths
                    }
                };

                output = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
            }

            return output;
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