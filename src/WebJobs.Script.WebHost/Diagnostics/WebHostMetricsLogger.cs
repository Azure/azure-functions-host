// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostMetricsLogger : IMetricsLogger, IDisposable
    {
        private readonly MetricsEventManager _metricsEventManager;
        private bool disposed = false;

        public WebHostMetricsLogger(IOptionsMonitor<AppServiceOptions> appServiceOptions, IEventGenerator eventGenerator, IMetricsPublisher metricsPublisher, ILinuxContainerActivityPublisher linuxContainerActivityPublisher, ILoggerFactory loggerFactory)
            : this(appServiceOptions, eventGenerator, metricsPublisher, linuxContainerActivityPublisher, 5, loggerFactory)
        {
        }

        public WebHostMetricsLogger(MetricsEventManager eventManager)
        {
            _metricsEventManager = eventManager;
        }

        protected WebHostMetricsLogger(IOptionsMonitor<AppServiceOptions> appServiceOptions, IEventGenerator eventGenerator, IMetricsPublisher metricsPublisher, ILinuxContainerActivityPublisher linuxContainerActivityPublisher, int metricEventIntervalInSeconds, ILoggerFactory loggerFactory)
        {
            _metricsEventManager = new MetricsEventManager(appServiceOptions, eventGenerator, metricEventIntervalInSeconds, metricsPublisher, linuxContainerActivityPublisher, loggerFactory.CreateLogger<MetricsEventManager>());
        }

        public object BeginEvent(string eventName, string functionName = null, string data = null)
        {
            return _metricsEventManager.BeginEvent(eventName, functionName, data);
        }

        public void BeginEvent(MetricEvent metricEvent)
        {
            if (metricEvent is FunctionStartedEvent startedEvent)
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
            if (metricEvent is FunctionStartedEvent completedEvent)
            {
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