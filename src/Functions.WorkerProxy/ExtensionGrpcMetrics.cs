// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Metrics;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Records transport-level measurements for worker-facing extension gRPC calls.
/// </summary>
internal sealed class ExtensionGrpcMetrics
{
    internal const string MeterName = "Microsoft.Azure.Functions.WorkerProxy.ExtensionGrpc";
    internal const string MeterVersion = "1.0.0";
    internal const string ActiveCallsInstrumentName = "azure.functions.worker_proxy.extension_rpc.calls.active";
    internal const string CallDurationInstrumentName = "azure.functions.worker_proxy.extension_rpc.call.duration";
    internal const string CallOpenDurationInstrumentName =
        "azure.functions.worker_proxy.extension_rpc.call.open.duration";

    /// <summary>
    /// Initializes extension gRPC metrics using a factory-owned meter.
    /// </summary>
    /// <param name="meterFactory">The factory that owns the meter lifetime.</param>
    public ExtensionGrpcMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

#pragma warning disable CA2000 // IMeterFactory owns the meter lifetime.
        Meter meter = meterFactory.Create(MeterName, MeterVersion);
#pragma warning restore CA2000

        ActiveCalls = new(meter);
        CallDuration = new(meter);
        CallOpenDuration = new(meter);
    }

    /// <summary>
    /// Gets the active-call counter.
    /// </summary>
    public ActiveCallsCounter ActiveCalls { get; }

    /// <summary>
    /// Gets the total call-duration histogram.
    /// </summary>
    public CallDurationHistogram CallDuration { get; }

    /// <summary>
    /// Gets the call-open-duration histogram.
    /// </summary>
    public CallOpenDurationHistogram CallOpenDuration { get; }

    /// <summary>
    /// Records the number of active extension gRPC calls.
    /// </summary>
    /// <remarks>
    /// Initializes the active-call counter.
    /// </remarks>
    /// <param name="meter">The meter used to create the counter.</param>
    internal sealed class ActiveCallsCounter(Meter meter)
    {
        private readonly UpDownCounter<long> _counter = meter.CreateUpDownCounter<long>(
            ActiveCallsInstrumentName,
            unit: "{call}",
            description: "Number of worker-facing extension gRPC calls currently relayed by this proxy.");

        /// <summary>
        /// Records that an extension gRPC call started relaying.
        /// </summary>
        public void Increment() => _counter.Add(1);

        /// <summary>
        /// Records that an extension gRPC call stopped relaying.
        /// </summary>
        public void Decrement() => _counter.Add(-1);
    }

    /// <summary>
    /// Records extension gRPC call durations.
    /// </summary>
    /// <remarks>
    /// Initializes the call-duration histogram.
    /// </remarks>
    /// <param name="meter">The meter used to create the histogram.</param>
    internal sealed class CallDurationHistogram(Meter meter)
    {
        private readonly Histogram<double> _histogram = meter.CreateHistogram<double>(
            CallDurationInstrumentName,
            unit: "ms",
            description: "Time spent relaying a worker-facing extension gRPC call, including stream assignment.");

        /// <summary>
        /// Records a call duration.
        /// </summary>
        /// <param name="durationMilliseconds">The total relay duration in milliseconds.</param>
        public void Record(double durationMilliseconds) => _histogram.Record(durationMilliseconds);
    }

    /// <summary>
    /// Records extension gRPC call-open durations.
    /// </summary>
    /// <remarks>
    /// Initializes the call-open-duration histogram.
    /// </remarks>
    /// <param name="meter">The meter used to create the histogram.</param>
    internal sealed class CallOpenDurationHistogram(Meter meter)
    {
        private readonly Histogram<double> _histogram = meter.CreateHistogram<double>(
            CallOpenDurationInstrumentName,
            unit: "ms",
            description: "Time spent assigning a worker-facing extension gRPC call to the host stream.");

        /// <summary>
        /// Records a call-open duration.
        /// </summary>
        /// <param name="durationMilliseconds">The stream-assignment latency in milliseconds.</param>
        public void Record(double durationMilliseconds) => _histogram.Record(durationMilliseconds);
    }
}
