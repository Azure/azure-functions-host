// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using AIMetric = Microsoft.ApplicationInsights.Metric;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// A meter listener which exports metrics to Application Insights.
    /// </summary>
    public sealed class ApplicationInsightsMeterListener : ITelemetryModule, IAsyncDisposable
    {
        private readonly MeterListener _listener;
        private readonly ApplicationInsightsMeterOptions _options;
        private readonly CancellationTokenSource _shutdown = new();

        private Task _exportTask = Task.CompletedTask;
        private TelemetryClient _client = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInsightsMeterListener"/> class.
        /// </summary>
        /// <param name="lifetime">The application lifetime.</param>
        /// <param name="options">The options.</param>
        public ApplicationInsightsMeterListener(
            IHostApplicationLifetime lifetime,
            IOptions<ApplicationInsightsMeterOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(lifetime);

            _shutdown = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
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
            where T : struct
        {
            return (instrument, value, tags, state) =>
            {
                if (state is not ApplicationInsightsMeterListener listener)
                {
                    return;
                }

                listener.Publish(instrument, value, tags);
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

        private void Publish<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            if (instrument is null)
            {
                return;
            }

            static bool TrackValue(AIMetric metric, double d)
            {
                metric.TrackValue(d);
                return true;
            }

            MetricIdentifier identifier = GetIdentifier(instrument, tags);
            AIMetric metric = _client.GetMetric(identifier);

            double d = MetricHelpers.ConvertToDouble(value);
            if (tags.Length == 0)
            {
                metric.TrackValue(d);
                return;
            }

            // All the calls are unrolled to avoid allocations.
            _ = tags.Length switch
            {
                0 => TrackValue(metric, d), // need to massage return type for switch.
                1 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0)),
                2 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1)),
                3 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2)),
                4 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3)),
                5 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3),
                    GetValueOrDefault(tags, 4)),
                6 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3),
                    GetValueOrDefault(tags, 4),
                    GetValueOrDefault(tags, 5)),
                7 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3),
                    GetValueOrDefault(tags, 4),
                    GetValueOrDefault(tags, 5),
                    GetValueOrDefault(tags, 6)),
                8 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3),
                    GetValueOrDefault(tags, 4),
                    GetValueOrDefault(tags, 5),
                    GetValueOrDefault(tags, 6),
                    GetValueOrDefault(tags, 7)),
                9 => metric.TrackValue(
                    d,
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3),
                    GetValueOrDefault(tags, 4),
                    GetValueOrDefault(tags, 5),
                    GetValueOrDefault(tags, 6),
                    GetValueOrDefault(tags, 7),
                    GetValueOrDefault(tags, 8)),
                _ => metric.TrackValue(
                    d, /* only track first 10 dimensions */
                    GetValueOrDefault(tags, 0),
                    GetValueOrDefault(tags, 1),
                    GetValueOrDefault(tags, 2),
                    GetValueOrDefault(tags, 3),
                    GetValueOrDefault(tags, 4),
                    GetValueOrDefault(tags, 5),
                    GetValueOrDefault(tags, 6),
                    GetValueOrDefault(tags, 7),
                    GetValueOrDefault(tags, 8),
                    GetValueOrDefault(tags, 9)),
            };
        }

        private static MetricIdentifier GetIdentifier(
            Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            // App insights only supports up to 10 dimensions. We also want to avoid any extra allocation here, so we
            // use the explicit ctor and not the IList<string> accepting one.
            return new MetricIdentifier(
                instrument.Meter.Name,
                instrument.Name,
                tags.Length > 0 ? tags[0].Key : null,
                tags.Length > 1 ? tags[1].Key : null,
                tags.Length > 2 ? tags[2].Key : null,
                tags.Length > 3 ? tags[3].Key : null,
                tags.Length > 4 ? tags[4].Key : null,
                tags.Length > 5 ? tags[5].Key : null,
                tags.Length > 6 ? tags[6].Key : null,
                tags.Length > 7 ? tags[7].Key : null,
                tags.Length > 8 ? tags[8].Key : null,
                tags.Length > 9 ? tags[9].Key : null);
        }

        private static string GetValueOrDefault(ReadOnlySpan<KeyValuePair<string, object?>> tags, int index)
            => tags.Length > index && tags[index].Value != null
                ? tags[index].Value?.ToString() ?? string.Empty : string.Empty;
    }
}