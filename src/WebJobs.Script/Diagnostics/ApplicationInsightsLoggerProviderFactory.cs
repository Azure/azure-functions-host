// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    //public class ApplicationInsightsLoggerProviderFactory : ILoggerProviderFactory
    //{
    //    private readonly ScriptSettingsManager _settingsManager;
    //    private readonly IMetricsLogger _metricsLogger;

    //    public ApplicationInsightsLoggerProviderFactory(ScriptSettingsManager settingsManager, IMetricsLogger metricsLogger)
    //    {
    //        _settingsManager = settingsManager;
    //        _metricsLogger = metricsLogger;
    //    }

    //    public ILoggerProvider Create()
    //    {
    //        IList<ILoggerProvider> providers = new List<ILoggerProvider>();

    //        // Automatically register App Insights if the key is present
    //        if (!string.IsNullOrEmpty(_settingsManager.ApplicationInsightsInstrumentationKey))
    //        {
    //            _metricsLogger.LogEvent(MetricEventNames.ApplicationInsightsEnabled);

    //            // TODO: DI (FACAVAL) BrettSam to review
    //            //ITelemetryClientFactory clientFactory = scriptConfig.HostOptions.GetService<ITelemetryClientFactory>() ??
    //            //    new ScriptTelemetryClientFactory(settingsManager.ApplicationInsightsInstrumentationKey, scriptConfig.ApplicationInsightsSamplingSettings, scriptConfig.LogFilter.Filter);

    //            // providers.Add(new ApplicationInsightsLoggerProvider(clientFactory));
    //        }
    //        else
    //        {
    //            _metricsLogger?.LogEvent(MetricEventNames.ApplicationInsightsDisabled);
    //        }

    //        // console logging defaults to false, except for self host
    //        bool enableConsole = scriptConfig.IsSelfHost;
    //        string configValue = settingsManager.Configuration.GetSection(ScriptConstants.ConsoleLoggingMode).Value;
    //        if (!string.IsNullOrEmpty(configValue))
    //        {
    //            // if it has been explicitly configured that value overrides default
    //            enableConsole = string.CompareOrdinal(configValue, "always") == 0 ? true : false;
    //        }
    //        if (ConsoleLoggingEnabled(scriptConfig, settingsManager))
    //        {
    //            providers.Add(new ConsoleLoggerProvider(scriptConfig.LogFilter.Filter, includeScopes: true));
    //        }

    //        return providers;
    //    }

    //    internal static bool ConsoleLoggingEnabled(ScriptHostOptions config, ScriptSettingsManager settingsManager)
    //    {
    //        // console logging defaults to false, except for self host
    //        bool enableConsole = config.IsSelfHost;

    //        string configValue = settingsManager.Configuration.GetSection(ScriptConstants.ConsoleLoggingMode).Value;
    //        if (!string.IsNullOrEmpty(configValue))
    //        {
    //            // if it has been explicitly configured that value overrides default
    //            enableConsole = string.Compare(configValue, "always", StringComparison.OrdinalIgnoreCase) == 0 ? true : false;
    //        }

    //        return enableConsole;
    //    }
    //}
}