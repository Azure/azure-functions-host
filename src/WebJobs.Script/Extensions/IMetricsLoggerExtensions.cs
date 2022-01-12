// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class IMetricsLoggerExtensions
    {
        public static IDisposable LatencyEvent(this IMetricsLogger metricsLogger, string eventName, string functionName = null)
        {
            return new DisposableEvent(eventName, functionName, metricsLogger);
        }

        private class DisposableEvent : IDisposable
        {
            private readonly object _metricEvent;
            private readonly IMetricsLogger _metricsLogger;
            private bool _disposed;

            public DisposableEvent(string eventName, string functionName, IMetricsLogger metricsLogger)
            {
                _metricEvent = metricsLogger.BeginEvent(eventName, functionName, Stopwatch.IsHighResolution ? @"{""IsStopwatchHighResolution"": True}" : @"{""IsStopwatchHighResolution"": False}");
                _metricsLogger = metricsLogger;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    if (_metricsLogger != null && _metricEvent != null)
                    {
                        _metricsLogger.EndEvent(_metricEvent);
                    }
                }
                _disposed = true;
            }
        }
    }
}
