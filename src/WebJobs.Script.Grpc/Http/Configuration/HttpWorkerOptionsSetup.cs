// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpWorkerOptionsSetup : IConfigureOptions<HttpWorkerOptions>, IValidateOptions<HttpWorkerOptions>
    {
        private readonly IEnvironment _environment;
        private IConfiguration _configuration;
        private ILogger _logger;
        private IMetricsLogger _metricsLogger;
        private ScriptJobHostOptions _scriptJobHostOptions;
        private string argumentsSectionName = $"{WorkerConstants.WorkerDescription}:arguments";
        private string workerArgumentsSectionName = $"{WorkerConstants.WorkerDescription}:workerArguments";

        public HttpWorkerOptionsSetup(IOptions<ScriptJobHostOptions> scriptJobHostOptions, IConfiguration configuration, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, IEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(environment);
            _scriptJobHostOptions = scriptJobHostOptions.Value;
            _configuration = configuration;
            _metricsLogger = metricsLogger;
            _logger = loggerFactory.CreateLogger<HttpWorkerOptionsSetup>();
            _environment = environment;
        }

        public void Configure(HttpWorkerOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var httpWorkerSection = jobHostSection.GetSection(ConfigurationSectionNames.HttpWorker);
            var customHandlerSection = jobHostSection.GetSection(ConfigurationSectionNames.CustomHandler);

            if (httpWorkerSection.Exists() && customHandlerSection.Exists())
            {
                _logger.LogWarning($"Both {ConfigurationSectionNames.HttpWorker} and {ConfigurationSectionNames.CustomHandler} sections are spefified in {ScriptConstants.HostMetadataFileName} file. {ConfigurationSectionNames.CustomHandler} takes precedence.");
            }

            if (customHandlerSection.Exists())
            {
                _metricsLogger.LogEvent(MetricEventNames.CustomHandlerConfiguration);
                ConfigureWorkerDescription(options, customHandlerSection);
                if (options.Type == CustomHandlerType.None)
                {
                    // CustomHandlerType.None is only for maintaining backward compatibility with httpWorker section.
                    _logger.LogWarning($"CustomHandlerType {CustomHandlerType.None} is not supported. Defaulting to {CustomHandlerType.Http}.");
                    options.Type = CustomHandlerType.Http;
                }
                return;
            }

            if (httpWorkerSection.Exists())
            {
                // TODO: Add aka.ms/link to new docs
                _logger.LogWarning($"Section {ConfigurationSectionNames.HttpWorker} will be deprecated. Please use {ConfigurationSectionNames.CustomHandler} section.");
                ConfigureWorkerDescription(options, httpWorkerSection);
                // Explicity set this to None to differentiate between customHandler and httpWorker options.
                options.Type = CustomHandlerType.None;
            }
        }

        private void ConfigureWorkerDescription(HttpWorkerOptions options, IConfigurationSection workerSection)
        {
            workerSection.Bind(options);

            var workerRuntime = _environment.GetFunctionsWorkerRuntime();
            if (string.Equals(workerRuntime, RpcWorkerConstants.CustomHandlerLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                options.CustomRoutesEnabled = true;
            }

            if (options.Http?.Routes is not null && options.Http.Routes.Any())
            {
                foreach (var route in options.Http.Routes)
                {
                    route.AuthorizationLevel ??= options.Http.DefaultAuthorizationLevel;
                }
            }

            HttpWorkerDescription httpWorkerDescription = options.Description;

            if (httpWorkerDescription == null)
            {
                throw new HostConfigurationException($"Missing worker Description.");
            }

            var argumentsList = GetArgumentList(workerSection, argumentsSectionName);
            if (argumentsList != null)
            {
                httpWorkerDescription.Arguments = argumentsList;
            }

            var workerArgumentList = GetArgumentList(workerSection, workerArgumentsSectionName);
            if (workerArgumentList != null)
            {
                httpWorkerDescription.WorkerArguments = workerArgumentList;
            }

            httpWorkerDescription.ApplyDefaultsAndValidate(_scriptJobHostOptions.RootScriptPath, _logger);

            // Set default working directory to function app root.
            if (string.IsNullOrEmpty(httpWorkerDescription.WorkingDirectory))
            {
                httpWorkerDescription.WorkingDirectory = _scriptJobHostOptions.RootScriptPath;
            }
            else
            {
                // Compute working directory relative to fucntion app root.
                if (!Path.IsPathRooted(httpWorkerDescription.WorkingDirectory))
                {
                    httpWorkerDescription.WorkingDirectory = Path.Combine(_scriptJobHostOptions.RootScriptPath, httpWorkerDescription.WorkingDirectory);
                }
            }

            options.Arguments = new WorkerProcessArguments()
            {
                ExecutablePath = options.Description.DefaultExecutablePath,
                WorkerPath = options.Description.DefaultWorkerPath
            };

            options.Arguments.ExecutableArguments.AddRange(options.Description.Arguments);
            options.Arguments.WorkerArguments.AddRange(options.Description.WorkerArguments);
        }

        private static List<string> GetArgumentList(IConfigurationSection workerConfigSection, string argumentSectionName)
        {
            var argumentsSection = workerConfigSection.GetSection(argumentSectionName);
            if (argumentsSection.Exists() && argumentsSection?.Value != null)
            {
                try
                {
                    return JsonConvert.DeserializeObject<List<string>>(argumentsSection.Value);
                }
                catch
                {
                }
            }
            return null;
        }

        public ValidateOptionsResult Validate(string name, HttpWorkerOptions options)
        {
            if (options.IsPortManuallySet)
            {
                var port = options.Port;

                if (port == 0 || !WorkerUtilities.CanBindToPort(port))
                {
                    throw new HostConfigurationException($"Unable to bind to port {port} specified in configuration. Please specify a different port or remove the section to allow dynamic binding of port.");
                }

                _logger.LogInformation("Using port {port} specified via configuration for custom handler.", port);
            }

            return ValidateOptionsResult.Success;
        }
    }
}