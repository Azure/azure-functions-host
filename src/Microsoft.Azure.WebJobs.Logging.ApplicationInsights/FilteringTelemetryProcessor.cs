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
                if (!telemetry.Properties.TryGetValue(LoggingKeys.CategoryName, out string categoryName))
                {
                    // If no category is specified, it will be filtered on by the default filter
                    categoryName = string.Empty;
                }

                // Extract the log level, category, and apply the filter
                if (telemetry.Properties.TryGetValue(LoggingKeys.LogLevel, out string logLevelString) &&
                    Enum.TryParse(logLevelString, out LogLevel logLevel))
                {
                    enabled = _filter(categoryName, logLevel);
                }
            }

            return enabled;
        }
    }
}
