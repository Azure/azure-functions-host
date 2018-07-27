// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal class ScriptHostOptionsSetup : IConfigureOptions<ScriptHostOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IOptions<ScriptWebHostOptions> _webHostOptions;

        public ScriptHostOptionsSetup(IConfiguration configuration, IOptions<ScriptWebHostOptions> webHostOptions)
        {
            _configuration = configuration;
            _webHostOptions = webHostOptions;
        }

        public void Configure(ScriptHostOptions options)
        {
            // Bind to all configuration properties
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);

            if (jobHostSection != null)
            {
                jobHostSection.Bind(options);

                var fileLoggingMode = jobHostSection.GetSection(ConfigurationSectionNames.JobHostLogger)
                    ?.GetValue<FileLoggingMode?>("fileLoggingMode");
                if (fileLoggingMode != null)
                {
                    options.FileLoggingMode = fileLoggingMode.Value;
                }
            }

            // Set the root script path to the value the runtime was initialized with:
            ScriptWebHostOptions webHostOptions = _webHostOptions.Value;
            options.RootScriptPath = webHostOptions.ScriptPath;
            options.RootLogPath = webHostOptions.LogPath;
            options.IsSelfHost = webHostOptions.IsSelfHost;
            options.TestDataPath = webHostOptions.TestDataPath;
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

        //    value = null;
        //    if (config.TryGetValue("functionTimeout", out value))
        //    {
        //        TimeSpan requestedTimeout = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);

        //        // Only apply limits if this is Dynamic.
        //        if (ScriptSettingsManager.Instance.IsDynamicSku && (requestedTimeout < MinFunctionTimeout || requestedTimeout > MaxFunctionTimeout))
        //        {
        //            string message = $"{nameof(scriptConfig.FunctionTimeout)} must be between {MinFunctionTimeout} and {MaxFunctionTimeout}.";
        //            throw new ArgumentException(message);
        //        }

        //        scriptConfig.FunctionTimeout = requestedTimeout;
        //    }
        //    else if (ScriptSettingsManager.Instance.IsDynamicSku)
        //    {
        //        // Apply a default if this is running on Dynamic.
        //        scriptConfig.FunctionTimeout = DefaultFunctionTimeout;
        //    }
        //    scriptConfig.HostOptions.FunctionTimeout = ScriptHost.CreateTimeoutConfiguration(scriptConfig);

        //    ApplyLanguageWorkersConfig(config, scriptConfig, logger);
        //    ApplyLoggerConfig(config, scriptConfig);
        //    ApplyApplicationInsightsConfig(config, scriptConfig);
        //}

        // TODO: DI (FACAVAL) All configuration needs to move to initialization
        //internal static void ApplyLoggerConfig(JObject configJson, ScriptHostConfiguration scriptConfig)
        //{
        //    scriptConfig.LogFilter = new LogCategoryFilter();
        //    JObject configSection = (JObject)configJson["logger"];
        //    JToken value;
        //    if (configSection != null)
        //    {
        //        JObject filterSection = (JObject)configSection["categoryFilter"];
        //        if (filterSection != null)
        //        {
        //            if (filterSection.TryGetValue("defaultLevel", out value))
        //            {
        //                LogLevel level;
        //                if (Enum.TryParse(value.ToString(), out level))
        //                {
        //                    scriptConfig.LogFilter.DefaultLevel = level;
        //                }
        //            }

        //            if (filterSection.TryGetValue("categoryLevels", out value))
        //            {
        //                scriptConfig.LogFilter.CategoryLevels.Clear();
        //                foreach (var prop in ((JObject)value).Properties())
        //                {
        //                    LogLevel level;
        //                    if (Enum.TryParse(prop.Value.ToString(), out level))
        //                    {
        //                        scriptConfig.LogFilter.CategoryLevels[prop.Name] = level;
        //                    }
        //                }
        //            }
        //        }

        //        JObject aggregatorSection = (JObject)configSection["aggregator"];
        //        if (aggregatorSection != null)
        //        {
        //            if (aggregatorSection.TryGetValue("batchSize", out value))
        //            {
        //                scriptConfig.HostOptions.Aggregator.BatchSize = (int)value;
        //            }

        //            if (aggregatorSection.TryGetValue("flushTimeout", out value))
        //            {
        //                scriptConfig.HostOptions.Aggregator.FlushTimeout = TimeSpan.Parse(value.ToString());
        //            }
        //        }

        //        if (configSection.TryGetValue("fileLoggingMode", out value))
        //        {
        //            FileLoggingMode fileLoggingMode;
        //            if (Enum.TryParse<FileLoggingMode>((string)value, true, out fileLoggingMode))
        //            {
        //                scriptConfig.FileLoggingMode = fileLoggingMode;
        //            }
        //        }
        //    }
        //}
    }
}
