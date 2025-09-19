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
    public sealed class ApplicationInsightsMetricExporter : ITelemetryModule, IAsyncDisposable
    {
        private readonly MeterListener _listener;
        private readonly ApplicationInsightsMetricExporterOptions _options;
        private readonly CancellationTokenSource _shutdown = new();

        private Task _exportTask = Task.CompletedTask;
        private TelemetryClient _client = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInsightsMetricExporter"/> class.
        /// </summary>
        /// <param name="lifetime">The application lifetime.</param>
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

        public void Initialize(TelemetryConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _client = new TelemetryClient(configuration);
            _exportTask = CollectAsync(_shutdown.Token);
            _listener.Start();
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Dispose();

            await _shutdown.CancelNoThrowAsync();
            await _exportTask.ConfigureAwait(false);
            await _client.FlushAsync(default).ConfigureAwait(false);
            _shutdown.Dispose();
        }

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
                    _listener.RecordObservableInstruments();
                    await Task.Delay(_options.CollectInterval, cancellation);
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    // swallow exceptions
                }
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
