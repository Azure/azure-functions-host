// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides ways to plug into the ScriptHost ILoggerFactory initialization.
    /// </summary>
    public class DefaultLoggerProviderFactory : ILoggerProviderFactory
    {
        public virtual IEnumerable<ILoggerProvider> CreateLoggerProviders(string hostInstanceId, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager,
            Func<bool> isFileLoggingEnabled, Func<bool> isPrimary)
        {
            IList<ILoggerProvider> providers = new List<ILoggerProvider>();

            IMetricsLogger metricsLogger = scriptConfig.HostConfig.GetService<IMetricsLogger>();

            // Automatically register App Insights if the key is present
            if (!string.IsNullOrEmpty(settingsManager?.ApplicationInsightsInstrumentationKey))
            {
                metricsLogger?.LogEvent(MetricEventNames.ApplicationInsightsEnabled);

                ITelemetryClientFactory clientFactory = scriptConfig.HostConfig.GetService<ITelemetryClientFactory>() ??
                    new ScriptTelemetryClientFactory(settingsManager.ApplicationInsightsInstrumentationKey, scriptConfig.ApplicationInsightsSamplingSettings, scriptConfig.LogFilter.Filter);

                providers.Add(new ApplicationInsightsLoggerProvider(clientFactory));
            }
            else
            {
                metricsLogger?.LogEvent(MetricEventNames.ApplicationInsightsDisabled);
            }

            providers.Add(new FunctionFileLoggerProvider(hostInstanceId, scriptConfig.RootLogPath, isFileLoggingEnabled, isPrimary));
            providers.Add(new HostFileLoggerProvider(hostInstanceId, scriptConfig.RootLogPath, isFileLoggingEnabled));

            // console logging defaults to false, except for self host
            bool enableConsole = scriptConfig.IsSelfHost;
            string configValue = settingsManager.Configuration.GetSection(ScriptConstants.ConsoleLoggingMode).Value;
            if (!string.IsNullOrEmpty(configValue))
            {
                // if it has been explicitly configured that value overrides default
                enableConsole = string.CompareOrdinal(configValue, "always") == 0 ? true : false;
            }
            if (ConsoleLoggingEnabled(scriptConfig, settingsManager))
            {
                providers.Add(new ConsoleLoggerProvider(scriptConfig.LogFilter.Filter, includeScopes: true));
            }

            return providers;
        }

        internal static bool ConsoleLoggingEnabled(ScriptHostConfiguration config, ScriptSettingsManager settingsManager)
        {
            // console logging defaults to false, except for self host
            bool enableConsole = config.IsSelfHost;

            string configValue = settingsManager.Configuration.GetSection(ScriptConstants.ConsoleLoggingMode).Value;
            if (!string.IsNullOrEmpty(configValue))
            {
                // if it has been explicitly configured that value overrides default
                enableConsole = string.Compare(configValue, "always", StringComparison.OrdinalIgnoreCase) == 0 ? true : false;
            }

            return enableConsole;
        }
    }
}