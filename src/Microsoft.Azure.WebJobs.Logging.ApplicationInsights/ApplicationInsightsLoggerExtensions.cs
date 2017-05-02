// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.WebJobs.Host.Loggers;

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// Extensions for adding the <see cref="ApplicationInsightsLoggerProvider"/> to an <see cref="ILoggerFactory"/>. 
    /// </summary>
    public static class ApplicationInsightsLoggerExtensions
    {
        /// <summary>
        /// Registers an <see cref="ApplicationInsightsLoggerProvider"/> with an <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <param name="loggerFactory">The factory.</param>
        /// <param name="instrumentationKey">The Application Insights instrumentation key.</param>
        /// <param name="filter">A filter that returns true if a message with the specified <see cref="LogLevel"/>
        /// and category should be logged. You can use <see cref="LogCategoryFilter.Filter(string, LogLevel)"/>
        /// or write a custom filter.</param>
        /// <returns>A <see cref="ILoggerFactory"/> for chaining additional operations.</returns>
        public static ILoggerFactory AddApplicationInsights(
            this ILoggerFactory loggerFactory,
            string instrumentationKey,
            Func<string, LogLevel, bool> filter)
        {
            ITelemetryClientFactory defaultFactory = new DefaultTelemetryClientFactory(instrumentationKey, new SamplingPercentageEstimatorSettings());
            return AddApplicationInsights(loggerFactory, defaultFactory, filter);
        }

        /// <summary>
        /// Registers an <see cref="ApplicationInsightsLoggerProvider"/> with an <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <param name="loggerFactory">The factory.</param>        
        /// <param name="telemetryClientFactory">The factory to use when creating the <see cref="TelemetryClient"/> </param>
        /// <param name="filter">A filter that returns true if a message with the specified <see cref="LogLevel"/>
        /// and category should be logged. You can use <see cref="LogCategoryFilter.Filter(string, LogLevel)"/>
        /// or write a custom filter.</param>
        /// <returns>A <see cref="ILoggerFactory"/> for chaining additional operations.</returns>
        public static ILoggerFactory AddApplicationInsights(
            this ILoggerFactory loggerFactory,
            ITelemetryClientFactory telemetryClientFactory,
            Func<string, LogLevel, bool> filter)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            // Note: LoggerFactory calls Dispose() on all registered providers.
            loggerFactory.AddProvider(new ApplicationInsightsLoggerProvider(filter, telemetryClientFactory));

            return loggerFactory;
        }
    }
}
