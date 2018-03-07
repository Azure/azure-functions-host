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
        public virtual IEnumerable<ILoggerProvider> CreateLoggerProviders(ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager,
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

            providers.Add(new FunctionFileLoggerProvider(scriptConfig.RootLogPath, isFileLoggingEnabled, isPrimary));
            providers.Add(new HostFileLoggerProvider(scriptConfig.RootLogPath, isFileLoggingEnabled));

            if (settingsManager.Configuration.GetSection("host:logger:consoleLoggingMode").Value == "always")
            {
                providers.Add(new ConsoleLoggerProvider(scriptConfig.LogFilter.Filter, includeScopes: true));
            }

            return providers;
        }
    }
}