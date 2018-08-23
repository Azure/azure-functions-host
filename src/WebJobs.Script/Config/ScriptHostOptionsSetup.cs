// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.Rpc.LanguageWorkerConstants;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class ScriptHostOptionsSetup : IConfigureOptions<ScriptJobHostOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly IOptions<ScriptApplicationHostOptions> _applicationHostOptions;

        internal static readonly TimeSpan MinFunctionTimeout = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan DefaultFunctionTimeout = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan MaxFunctionTimeout = TimeSpan.FromMinutes(10);

        public ScriptHostOptionsSetup(IConfiguration configuration, IEnvironment environment, IOptions<ScriptApplicationHostOptions> applicationHostOptions)
        {
            _configuration = configuration;
            _environment = environment;
            _applicationHostOptions = applicationHostOptions;
        }

        public void Configure(ScriptJobHostOptions options)
        {
            // Add the standard built in watched directories set to any the user may have specified
            options.WatchDirectories.Add("node_modules");

            // Set default logging mode
            options.FileLoggingMode = FileLoggingMode.DebugOnly;

            // Bind to all configuration properties
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);

            if (jobHostSection != null)
            {
                jobHostSection.Bind(options);

                var fileLoggingMode = jobHostSection.GetSection(ConfigurationSectionNames.Logging)
                    ?.GetValue<FileLoggingMode?>("fileLoggingMode");
                if (fileLoggingMode != null)
                {
                    options.FileLoggingMode = fileLoggingMode.Value;
                }
            }

            // FunctionTimeout
            ConfigureFunctionTimeout(jobHostSection, options);

            // Worker configuration
            ConfigureLanguageWorkers(jobHostSection, options);

            // Set the root script path to the value the runtime was initialized with:
            ScriptApplicationHostOptions webHostOptions = _applicationHostOptions.Value;
            options.RootScriptPath = webHostOptions.ScriptPath;
            options.RootLogPath = webHostOptions.LogPath;
            options.IsSelfHost = webHostOptions.IsSelfHost;
            options.TestDataPath = webHostOptions.TestDataPath;
        }

        private void ConfigureFunctionTimeout(IConfigurationSection jobHostSection, ScriptJobHostOptions options)
        {
            string value = jobHostSection.GetValue<string>("functionTimeout");
            if (value != null)
            {
                TimeSpan requestedTimeout = TimeSpan.Parse(value, CultureInfo.InvariantCulture);

                // Only apply limits if this is Dynamic.
                if (_environment.IsDynamic() && (requestedTimeout < MinFunctionTimeout || requestedTimeout > MaxFunctionTimeout))
                {
                    string message = $"{nameof(options.FunctionTimeout)} must be between {MinFunctionTimeout} and {MaxFunctionTimeout}.";
                    throw new ArgumentException(message);
                }

                options.FunctionTimeout = requestedTimeout;
            }
            else if (_environment.IsDynamic())
            {
                // Apply a default if this is running on Dynamic.
                options.FunctionTimeout = DefaultFunctionTimeout;
            }

            // TODO: DI: JobHostOptions need to me updated.
            //scriptConfig.HostOptions.FunctionTimeout = ScriptHost.CreateTimeoutConfiguration(scriptConfig);
        }

        private void ConfigureLanguageWorkers(IConfigurationSection rootConfig, ScriptJobHostOptions scriptOptions)
        {
            var languageWorkersSection = rootConfig.GetSection(LanguageWorkersSectionName);
            int requestedGrpcMaxMessageLength = _environment.IsDynamic() ? DefaultMaxMessageLengthBytesDynamicSku : DefaultMaxMessageLengthBytes;
            if (languageWorkersSection.Exists())
            {
                string value = languageWorkersSection.GetValue<string>("maxMessageLength");
                if (value != null)
                {
                    int valueInBytes = int.Parse(value) * 1024 * 1024;
                    if (_environment.IsDynamic())
                    {
                        // TODO: Because of a circular dependency, where logger providers are using ScriptHostOptions, we can't log from here
                        // need to remove the dependency from the various logger providers.
                        //string message = $"Cannot set {nameof(scriptOptions.MaxMessageLengthBytes)} on Consumption plan. Default MaxMessageLength: {DefaultMaxMessageLengthBytesDynamicSku} will be used";
                        //_logger?.LogWarning(message);
                    }
                    else
                    {
                        if (valueInBytes < 0 || valueInBytes > 2000 * 1024 * 1024)
                        {
                            // TODO: Because of a circular dependency, where logger providers are using ScriptHostOptions, we can't log from here
                            // need to remove the dependency from the various logger providers.
                            // Current grpc max message limits
                            //string message = $"MaxMessageLength must be between 4MB and 2000MB.Default MaxMessageLength: {DefaultMaxMessageLengthBytes} will be used";
                            //_logger?.LogWarning(message);
                        }
                        else
                        {
                            requestedGrpcMaxMessageLength = valueInBytes;
                        }
                    }
                }
            }
            scriptOptions.MaxMessageLengthBytes = requestedGrpcMaxMessageLength;
        }

        // TODO: DI (FACAVAL) Moved this from ScriptHost. Validate we're applying all configuration
        // Also noticed a lot of discrepancy between the original logic and the documentation and schema. We need to address
        // that. The current implemenation will match the original, which means that schema and docs will need to be udpated.
        //internal static void ApplyConfiguration(JObject config, ScriptHostConfiguration scriptConfig, ILogger logger = null)
        //{
        //    var hostConfig = scriptConfig.HostOptions;

        //    hostConfig.HostConfigMetadata = config;

        //    JArray functions = (JArray)config["functions"];
        //    if (functions != null && functions.Count > 0)
        //    {
        //        scriptConfig.Functions = new Collection<string>();
        //        foreach (var function in functions)
        //        {
        //            scriptConfig.Functions.Add((string)function);
        //        }
        //    }
        //    else
        //    {
        //        scriptConfig.Functions = null;
        //    }

        //    // We may already have a host id, but the one from the JSON takes precedence
        //    JToken hostId = (JToken)config["id"];
        //    if (hostId != null)
        //    {
        //        hostConfig.HostId = (string)hostId;
        //    }

        //    // Default AllowHostPartialStartup to true, but allow it
        //    // to be overridden by config
        //    hostConfig.AllowPartialHostStartup = true;
        //    JToken allowPartialHostStartup = (JToken)config["allowPartialHostStartup"];
        //    if (allowPartialHostStartup != null && allowPartialHostStartup.Type == JTokenType.Boolean)
        //    {
        //        hostConfig.AllowPartialHostStartup = (bool)allowPartialHostStartup;
        //    }

        //    JToken fileWatchingEnabled = (JToken)config["fileWatchingEnabled"];
        //    if (fileWatchingEnabled != null && fileWatchingEnabled.Type == JTokenType.Boolean)
        //    {
        //        scriptConfig.FileWatchingEnabled = (bool)fileWatchingEnabled;
        //    }

        //    // Configure the set of watched directories, adding the standard built in
        //    // set to any the user may have specified
        //    if (scriptConfig.WatchDirectories == null)
        //    {
        //        scriptConfig.WatchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        //    }
        //    scriptConfig.WatchDirectories.Add("node_modules");
        //    JToken watchDirectories = config["watchDirectories"];
        //    if (watchDirectories != null && watchDirectories.Type == JTokenType.Array)
        //    {
        //        foreach (JToken directory in watchDirectories.Where(p => p.Type == JTokenType.String))
        //        {
        //            scriptConfig.WatchDirectories.Add((string)directory);
        //        }
        //    }

        //    JToken nugetFallbackFolder = config["nugetFallbackFolder"];
        //    if (nugetFallbackFolder != null && nugetFallbackFolder.Type == JTokenType.String)
        //    {
        //        scriptConfig.NugetFallBackPath = (string)nugetFallbackFolder;
        //    }

        //    // Apply Singleton configuration
        //    JObject configSection = (JObject)config["singleton"];
        //    JToken value = null;
        //    if (configSection != null)
        //    {
        //        if (configSection.TryGetValue("lockPeriod", out value))
        //        {
        //            hostConfig.Singleton.LockPeriod = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //        if (configSection.TryGetValue("listenerLockPeriod", out value))
        //        {
        //            hostConfig.Singleton.ListenerLockPeriod = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //        if (configSection.TryGetValue("listenerLockRecoveryPollingInterval", out value))
        //        {
        //            hostConfig.Singleton.ListenerLockRecoveryPollingInterval = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //        if (configSection.TryGetValue("lockAcquisitionTimeout", out value))
        //        {
        //            hostConfig.Singleton.LockAcquisitionTimeout = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //        if (configSection.TryGetValue("lockAcquisitionPollingInterval", out value))
        //        {
        //            hostConfig.Singleton.LockAcquisitionPollingInterval = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //    }

        //    // Apply Host Health Montitor configuration
        //    configSection = (JObject)config["healthMonitor"];
        //    value = null;
        //    if (configSection != null)
        //    {
        //        if (configSection.TryGetValue("enabled", out value) && value.Type == JTokenType.Boolean)
        //        {
        //            scriptConfig.HostHealthMonitor.Enabled = (bool)value;
        //        }
        //        if (configSection.TryGetValue("healthCheckInterval", out value))
        //        {
        //            scriptConfig.HostHealthMonitor.HealthCheckInterval = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //        if (configSection.TryGetValue("healthCheckWindow", out value))
        //        {
        //            scriptConfig.HostHealthMonitor.HealthCheckWindow = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
        //        }
        //        if (configSection.TryGetValue("healthCheckThreshold", out value))
        //        {
        //            scriptConfig.HostHealthMonitor.HealthCheckThreshold = (int)value;
        //        }
        //        if (configSection.TryGetValue("counterThreshold", out value))
        //        {
        //            scriptConfig.HostHealthMonitor.CounterThreshold = (float)value;
        //        }
        //    }

        //    ApplyLanguageWorkersConfig(config, scriptConfig, logger);
        //}
    }
}
