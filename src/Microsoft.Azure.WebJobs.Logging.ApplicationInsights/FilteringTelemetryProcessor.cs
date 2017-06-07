// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    internal class FilteringTelemetryProcessor : ITelemetryProcessor
    {
        private Func<string, LogLevel, bool> _filter;
        private ITelemetryProcessor _next;

        public FilteringTelemetryProcessor(Func<string, LogLevel, bool> filter, ITelemetryProcessor next)
        {
            _filter = filter;
            _next = next;
        }

        public void Process(ITelemetry item)
        {
            if (IsEnabled(item))
            {
                _next.Process(item);
            }
        }

        private bool IsEnabled(ITelemetry item)
        {
            bool enabled = true;

            ISupportProperties telemetry = item as ISupportProperties;

            if (telemetry != null && _filter != null)
            {
                string categoryName = null;
                if (!telemetry.Properties.TryGetValue(LoggingKeys.CategoryName, out categoryName))
                {
                    // If no category is specified, it will be filtered by the default filter
                    categoryName = string.Empty;
                }

                // Extract the log level and apply the filter
                string logLevelString = null;
                LogLevel logLevel;
                if (telemetry.Properties.TryGetValue(LoggingKeys.LogLevel, out logLevelString) &&
                    Enum.TryParse(logLevelString, out logLevel))
                {
                    enabled = _filter(categoryName, logLevel);
                }
            }

            return enabled;
        }
    }
}
