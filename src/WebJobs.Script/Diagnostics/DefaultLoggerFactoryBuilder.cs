// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides ways to plug into the ScriptHost ILoggerFactory initialization.
    /// </summary>
    public class DefaultLoggerFactoryBuilder : ILoggerFactoryBuilder
    {
        /// <summary>
        /// Adds additional <see cref="ILoggerProvider"/>s to the <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <param name="factory">The <see cref="ILoggerFactory"/>.</param>
        /// <param name="scriptConfig">The configuration.</param>
        public virtual void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager)
        {
            IMetricsLogger metricsLogger = scriptConfig.HostConfig.GetService<IMetricsLogger>();

            // Automatically register App Insights if the key is present
            if (!string.IsNullOrEmpty(settingsManager?.ApplicationInsightsInstrumentationKey))
            {
                metricsLogger?.LogEvent(MetricEventNames.ApplicationInsightsEnabled);

                ITelemetryClientFactory clientFactory = scriptConfig.HostConfig.GetService<ITelemetryClientFactory>() ??
                    new ScriptTelemetryClientFactory(settingsManager.ApplicationInsightsInstrumentationKey, scriptConfig.LogFilter.Filter);

                scriptConfig.HostConfig.LoggerFactory.AddApplicationInsights(clientFactory);
            }
            else
            {
                metricsLogger?.LogEvent(MetricEventNames.ApplicationInsightsDisabled);
            }
        }
    }
}