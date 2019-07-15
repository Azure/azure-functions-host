// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostMetricsLogger : IMetricsLogger, IDisposable
    {
        private readonly MetricsEventManager _metricsEventManager;
        private bool disposed = false;

        public WebHostMetricsLogger(IEnvironment environment, IEventGenerator eventGenerator, IMetricsPublisher metricsPublisher)
            : this(environment, eventGenerator, metricsPublisher, 5)
        {
        }

        public WebHostMetricsLogger(MetricsEventManager eventManager)
        {
            _metricsEventManager = eventManager;
        }

        protected WebHostMetricsLogger(IEnvironment environment, IEventGenerator eventGenerator, IMetricsPublisher metricsPublisher, int metricEventIntervalInSeconds)
        {
            _metricsEventManager = new MetricsEventManager(environment, eventGenerator,  metricEventIntervalInSeconds, metricsPublisher);
        }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            return _metricsEventManager.BeginEvent(eventName, functionName, data);
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent startedEvent = metricEvent as FunctionStartedEvent;
            if (startedEvent != null)
            {
                startedEvent.Timestamp = DateTime.UtcNow;
                _metricsEventManager.FunctionStarted(startedEvent);
            }
        }

        public void EndEvent(object eventHandle)
        {
            _metricsEventManager.EndEvent(eventHandle);
        }

        public void EndEvent(MetricEvent metricEvent)
        {
            FunctionStartedEvent completedEvent = metricEvent as FunctionStartedEvent;
            if (completedEvent != null)
            {
                completedEvent.Duration = DateTime.UtcNow - completedEvent.Timestamp;
                _metricsEventManager.FunctionCompleted(completedEvent);
            }
            else
            {
                _metricsEventManager.EndEvent(metricEvent);
            }
        }

        public void LogEvent(MetricEvent metricEvent)
        {
            HostStarted hostStartedEvent = metricEvent as HostStarted;
            if (hostStartedEvent != null)
            {
                _metricsEventManager.HostStarted(hostStartedEvent.Host);
            }
        }

        public void LogEvent(string eventName, string functionName = null, string data = null)
        {
            _metricsEventManager.LogEvent(eventName, functionName, data);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (_metricsEventManager != null)
                    {
                        _metricsEventManager.Dispose();
                    }
                }

                disposed = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}