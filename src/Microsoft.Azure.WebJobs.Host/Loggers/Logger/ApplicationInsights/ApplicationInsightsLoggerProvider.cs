// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class ApplicationInsightsLoggerProvider : ILoggerProvider
    {
        private readonly TelemetryClient _client;
        private readonly Func<string, LogLevel, bool> _filter;

        public ApplicationInsightsLoggerProvider(string instrumentationKey, Func<string, LogLevel, bool> filter,
            ITelemetryClientFactory clientFactory, SamplingPercentageEstimatorSettings samplingSettings)
        {
            if (instrumentationKey == null)
            {
                throw new ArgumentNullException(nameof(instrumentationKey));
            }

            _client = clientFactory.Create(instrumentationKey, samplingSettings);
            _filter = filter;
        }

        public ILogger CreateLogger(string categoryName) => new ApplicationInsightsLogger(_client, categoryName, _filter);

        public void Dispose()
        {
        }
    }
}
