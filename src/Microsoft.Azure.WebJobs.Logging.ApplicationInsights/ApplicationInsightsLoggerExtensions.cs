// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;

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
            ITelemetryClientFactory defaultFactory = new DefaultTelemetryClientFactory(instrumentationKey, filter);

            return AddApplicationInsights(loggerFactory, defaultFactory);
        }

        /// <summary>
        /// Registers an <see cref="ApplicationInsightsLoggerProvider"/> with an <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <param name="loggerFactory">The factory.</param>        
        /// <param name="telemetryClientFactory">The factory to use when creating the <see cref="TelemetryClient"/> </param>
        /// <returns>A <see cref="ILoggerFactory"/> for chaining additional operations.</returns>
        public static ILoggerFactory AddApplicationInsights(
            this ILoggerFactory loggerFactory,
            ITelemetryClientFactory telemetryClientFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            // Note: LoggerFactory calls Dispose() on all registered providers.
            loggerFactory.AddProvider(new ApplicationInsightsLoggerProvider(telemetryClientFactory));

            return loggerFactory;
        }
    }
}
