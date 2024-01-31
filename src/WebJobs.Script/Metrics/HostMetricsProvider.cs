// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public class HostMetricsProvider : IHostMetricsProvider, IDisposable
    {
        private readonly MeterListener _meterListener;
        private readonly IServiceProvider _serviceProvider;
        private ConcurrentDictionary<string, long> _metricsCache = new();

        public HostMetricsProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _meterListener = new();

            Start();
        }

        public string FunctionGroup { get; private set; } = string.Empty;

        public string InstanceId { get; private set; } = string.Empty;

        public void Start()
        {
            _meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name is HostMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);

                    var funcGroupTag = instrument.Meter.Tags.FirstOrDefault(t => t.Key == TelemetryAttributes.AzureFunctionsGroup);
                    var instanceIdTag = instrument.Meter.Tags.FirstOrDefault(t => t.Key == TelemetryAttributes.ServiceInstanceId);
                    FunctionGroup = funcGroupTag.Value?.ToString() ?? string.Empty;
                    InstanceId = instanceIdTag.Value?.ToString() ?? string.Empty;
                }
            };

            _meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecordedLong);
            _meterListener.Start();
        }

        internal void OnMeasurementRecordedLong(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            if (instrument == null)
            {
                throw new ArgumentNullException(nameof(instrument));
            }

            AddOrUpdateMetricsCache(instrument.Name, measurement);
        }

        private void AddOrUpdateMetricsCache(string key, long value)
        {
            _metricsCache.AddOrUpdate(key,
                (k) =>
                {
                    if (value < 0)
                    {
                        return 0;
                    }
                    return value;
                },
                (k, oldValue) =>
                {
                    long newValue = oldValue + value;
                    if (newValue < 0)
                    {
                        return 0;
                    }
                    return newValue;
                });
        }

        public IReadOnlyDictionary<string, long>? GetHostMetricsOrNull()
        {
            if (!HasMetrics())
            {
                return null;
            }

            try
            {
                var functionActivityStatusProvider = _serviceProvider.GetScriptHostServiceOrNull<IFunctionActivityStatusProvider>();
                var functionActivityStatus = functionActivityStatusProvider.GetStatus();
                AddOrUpdateMetricsCache(HostMetrics.ActiveInvocationCount, functionActivityStatus.OutstandingInvocations);

                return new Dictionary<string, long>(_metricsCache);
            }
            finally
            {
                PurgeMetricsCache();
            }
        }

        public bool HasMetrics()
        {
            return _metricsCache.Count > 0;
        }

        private void PurgeMetricsCache()
        {
            _metricsCache.Clear();
        }

        public void Dispose()
        {
            _meterListener?.Dispose();
        }
    }
}
