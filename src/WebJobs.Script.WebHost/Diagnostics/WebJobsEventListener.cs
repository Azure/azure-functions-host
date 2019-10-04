// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Diagnostics
{
    internal class WebJobsEventListener : EventListener
    {
        private readonly ILogger _logger;

        public WebJobsEventListener(WebHostSystemLoggerProvider systemLoggerProvider)
        {
            _logger = systemLoggerProvider.CreateLogger(GetType().FullName);
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name.StartsWith("Microsoft-ApplicationInsights"))
            {
                EnableEvents(eventSource, EventLevel.Error);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            switch (eventData.Level)
            {
                case EventLevel.Critical:
                case EventLevel.Error:
                    if (TryFormatMessage(eventData, out string message))
                    {
                        _logger.LogError(message);
                    }
                    break;
                case EventLevel.Informational:
                case EventLevel.LogAlways:
                case EventLevel.Verbose:
                case EventLevel.Warning:
                default:
                    break;
            }
        }

        private bool TryFormatMessage(EventWrittenEventArgs eventData, out string message)
        {
            message = null;

            try
            {
                message = string.Format(CultureInfo.InvariantCulture, eventData.Message, eventData.Payload.ToArray());
                return true;
            }
            catch
            {
                // We don't want to throw here.
                _logger.LogError($"Error formatting {nameof(EventWrittenEventArgs)} for event '{eventData.EventName}'. Raw message: {eventData.Message}.");
            }

            return false;
        }
    }
}