// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Numerics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Metrics;
using AIMetric = Microsoft.ApplicationInsights.Metric;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal static class TelemetryClientExtensions
    {
        /// <summary>
        /// Tracks the <paramref name="instrument"/> as a metric with <paramref name="value"/> and
        /// <paramref name="tags"/> (dimensions).
        /// </summary>
        /// <param name="client">The client to track with.</param>
        /// <param name="instrument">The instrument to track a value for.</param>
        /// <param name="value">The value of the metric to track.</param>
        /// <param name="tags">The dimensions of the metric to track.</param>
        public static void TrackInstrument(
            this TelemetryClient client,
            Instrument instrument,
            double value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(instrument);
            MetricIdentifier identifier = GetIdentifier(instrument, tags, out string[] values);
            AIMetric metric = client.GetMetric(identifier);

            if (metric.TryGetDataSeries(out MetricSeries series, true, values))
            {
                series.TrackValue(value);
            }
        }

        private static MetricIdentifier GetIdentifier(
            Instrument instrument,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            out string[] values)
        {
            // App Insights supports a maximum of 10 dimensions. We will silently drop dimensions beyond that.
            int length = Math.Min(tags.Length, 10);

            // We use 10 variables to avoid collection allocations.
            string? dimension0 = null, dimension1 = null, dimension2 = null,
                dimension3 = null, dimension4 = null, dimension5 = null,
                dimension6 = null, dimension7 = null, dimension8 = null,
                dimension9 = null;

            // Avoiding array allocations for values as well, at least until we know how many we have.
            string? value0 = null, value1 = null, value2 = null,
                value3 = null, value4 = null, value5 = null,
                value6 = null, value7 = null, value8 = null,
                value9 = null;

            int count = 0; // this will be how many 'valid' dimensions we have.
            for (int i = 0; i < length; i++)
            {
                string? value = tags[i].Value?.ToString();
                string name = tags[i].Key;

                // Application Insights does not allow null/empty/whitespace dimension values.
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // We assign to the dimensionN and valueN variables based on the count of valid
                    // dimensions we've seen so far.
                    switch (count)
                    {
                        case 0: dimension0 = name; value0 = value; break;
                        case 1: dimension1 = name; value1 = value; break;
                        case 2: dimension2 = name; value2 = value; break;
                        case 3: dimension3 = name; value3 = value; break;
                        case 4: dimension4 = name; value4 = value; break;
                        case 5: dimension5 = name; value5 = value; break;
                        case 6: dimension6 = name; value6 = value; break;
                        case 7: dimension7 = name; value7 = value; break;
                        case 8: dimension8 = name; value8 = value; break;
                        case 9: dimension9 = name; value9 = value; break;
                    }

                    count++;
                }
            }

            if (count == 0)
            {
                // No dimensions, so return a simple identifier.
                values = [];
                return new MetricIdentifier(instrument.Meter.Name, instrument.Name);
            }

            values = new string[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = i switch
                {
                    0 => value0!,
                    1 => value1!,
                    2 => value2!,
                    3 => value3!,
                    4 => value4!,
                    5 => value5!,
                    6 => value6!,
                    7 => value7!,
                    8 => value8!,
                    9 => value9!,
                    _ => throw new InvalidOperationException(), // will never happen
                };
            }

            return new MetricIdentifier(
                instrument.Meter.Name,
                instrument.Name,
                dimension0,
                dimension1,
                dimension2,
                dimension3,
                dimension4,
                dimension5,
                dimension6,
                dimension7,
                dimension8,
                dimension9);
        }
    }
}
