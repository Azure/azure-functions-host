// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// A meter listener which exports metrics to Application Insights.
    /// </summary>
    public sealed partial class ApplicationInsightsMetricExporter : ITelemetryModule, IAsyncDisposable
    {
        private readonly MeterListener _listener;
        private readonly ApplicationInsightsMetricExporterOptions _options;
        private readonly CancellationTokenSource _shutdown = new();

        private Task _exportTask = Task.CompletedTask;
        private TelemetryClient _client = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInsightsMetricExporter"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        public ApplicationInsightsMetricExporter(IOptions<ApplicationInsightsMetricExporterOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _options = options.Value;
            _listener = new()
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (_options.ShouldListenTo(instrument))
                    {
                        Events.Log.SubscribedToInstrument(instrument);
                        listener.EnableMeasurementEvents(instrument, this);
                    }
                },
            };

            // All of the supported instrument value types.
            _listener.SetMeasurementEventCallback(CreateCallback<byte>());
            _listener.SetMeasurementEventCallback(CreateCallback<short>());
            _listener.SetMeasurementEventCallback(CreateCallback<int>());
            _listener.SetMeasurementEventCallback(CreateCallback<long>());
            _listener.SetMeasurementEventCallback(CreateCallback<float>());
            _listener.SetMeasurementEventCallback(CreateCallback<double>());
            _listener.SetMeasurementEventCallback(CreateCallback<decimal>());
        }

        /// <summary>
        /// Initializes this module, starting the meter listener and exporting process.
        /// </summary>
        /// <param name="configuration">The telemetry configuration.</param>
        public void Initialize(TelemetryConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ObjectDisposedException.ThrowIf(_shutdown.IsCancellationRequested, this);

            try
            {
                _client = new TelemetryClient(configuration);
                _listener.Start();
                _exportTask = CollectAsync(_shutdown.Token);
                Events.Log.MeterListeningStarted();
            }
            catch (Exception ex)
            {
                Events.Log.ErrorStartingMetricListener(ex);
                throw;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Events.Log.MeterListeningStopped();

            await _shutdown.CancelNoThrowAsync();
            await _exportTask.ConfigureAwait(false);
            CollectCore(); // collect one more time to ensure we get the last set of values.
            _listener.Dispose();

            if (_client is { } client)
            {
                await client.FlushAsync(default).ConfigureAwait(false);
                Events.Log.FlushedTelemetryClient();
            }

            _shutdown.Dispose();
        }

        /// <summary>
        /// Flushes the internal client.
        /// </summary>
        /// <remarks>
        /// Primarily for testing purposes.
        /// </remarks>
        internal void Flush() => _client.Flush();

        private static MeasurementCallback<T> CreateCallback<T>()
            where T : struct, INumber<T>, IConvertible
        {
            return (instrument, value, tags, state) =>
            {
                if (state is not ApplicationInsightsMetricExporter listener)
                {
                    return;
                }

                listener.Publish(instrument, value.ToDouble(null), tags);
            };
        }

        private async Task CollectAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    CollectCore();
                    await Task.Delay(_options.CollectInterval, cancellation);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private void CollectCore()
        {
            try
            {
                Events.Log.BeginCollectObservables();
                _listener.RecordObservableInstruments();
                Events.Log.EndCollectObservables();
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                Events.Log.FailedToCollectInstruments(ex);
            }
        }

        private void Publish(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (instrument is null)
            {
                return;
            }

            _client.TrackInstrument(instrument, value, tags);
        }
    }
}
