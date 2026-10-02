// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class ExtensionGrpcMetricsTests
{
    [Fact]
    public void RecordsCallMeasurements()
    {
        using var meterFactory = new TestMeterFactory();
        var metrics = new ExtensionGrpcMetrics(meterFactory);
        var measurements = new ConcurrentQueue<(string Name, double Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (string.Equals(
                    instrument.Meter.Name,
                    ExtensionGrpcMetrics.MeterName,
                    StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => measurements.Enqueue((instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) => measurements.Enqueue((instrument.Name, value)));
        listener.Start();

        metrics.CallOpenDuration.Record(12.5);
        metrics.ActiveCalls.Increment();
        metrics.CallDuration.Record(25.5);
        metrics.ActiveCalls.Decrement();

        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.CallOpenDurationInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == 12.5);
        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.CallDurationInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == 25.5);
        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.ActiveCallsInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == 1);
        Assert.Contains(
            measurements,
            measurement => string.Equals(
                measurement.Name,
                ExtensionGrpcMetrics.ActiveCallsInstrumentName,
                StringComparison.Ordinal)
                && measurement.Value == -1);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly ConcurrentBag<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);

            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
