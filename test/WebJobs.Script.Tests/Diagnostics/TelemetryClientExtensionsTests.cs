// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using AwesomeAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Metrics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Moq;
using Xunit;
using AIMetric = Microsoft.ApplicationInsights.Metric;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public sealed class TelemetryClientExtensionsTests : IDisposable
    {
        private readonly TelemetryClient _client;
        private readonly TelemetryConfiguration _config;
        private readonly List<ITelemetry> _items = [];

        public TelemetryClientExtensionsTests()
        {
            Mock<ITelemetryChannel> mockChannel = new();
            mockChannel.Setup(c => c.Send(It.IsAny<ITelemetry>()))
                .Callback<ITelemetry>(_items.Add);

            _config = new()
            {
                ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                TelemetryChannel = mockChannel.Object,
            };

            _client = new(_config);
        }

        public void Dispose()
        {
            _config.Dispose();
        }

        [Fact]
        public void TrackInstrument_NullClient_Throws()
        {
            // arrange
            using Meter meter = new("test.meter.nullclient");
            Counter<long> instrument = meter.CreateCounter<long>("test.metric");
            TelemetryClient client = null!;

            // act
            Action act = () => client.TrackInstrument(instrument, 1.0, []);

            // assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("client");
        }

        [Fact]
        public void TrackInstrument_NullInstrument_Throws()
        {
            // arrange
            Instrument instrument = null!;

            // act
            Action act = () => _client.TrackInstrument(instrument, 1.0, []);

            // assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("instrument");
            _items.Should().BeEmpty();
        }

        [Fact]
        public void TrackInstrument_NoDimensions_CreatesSeries()
        {
            // arrange
            double value = Random.Shared.NextDouble();
            using Meter meter = new("test.meter.nodims");
            Counter<long> counter = meter.CreateCounter<long>("test.metric.nodims");

            // act
            _client.TrackInstrument(counter, value, []);
            _client.Flush();

            // assert
            MetricIdentifier identifier = new(meter.Name, counter.Name);
            AIMetric metric = _client.GetMetric(identifier);
            bool exists = metric.TryGetDataSeries(out MetricSeries series, false, []);

            metric.SeriesCount.Should().Be(1);
            exists.Should().BeTrue();
            series.Should().NotBeNull();
            _items.Should().ContainSingle()
                .Which.Should().Satisfy<MetricTelemetry>(mt => VerifyMetric(mt, counter, value, null));
        }

        [Fact]
        public void TrackInstrument_Dimensions_CreatesSeries()
        {
            // arrange
            double value = Random.Shared.NextDouble();
            using Meter meter = new("test.meter.nodims");
            Counter<long> counter = meter.CreateCounter<long>("test.metric.nodims");
            KeyValuePair<string, object?>[] tags =
            [
                new("dim1", "value1"),
                new("dim2", "value2")
            ];

            // act
            _client.TrackInstrument(counter, value, tags);
            _client.Flush();

            // assert
            MetricIdentifier identifier = new(meter.Name, counter.Name, "dim1", "dim2");
            AIMetric metric = _client.GetMetric(identifier);
            bool exists = metric.TryGetDataSeries(out MetricSeries series, false, ["value1", "value2"]);

            metric.SeriesCount.Should().Be(2);
            exists.Should().BeTrue();
            series.Should().NotBeNull();

            Dictionary<string, string> expectedTags = tags.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString()!);
            _items.Should().ContainSingle()
                .Which.Should().Satisfy<MetricTelemetry>(mt => VerifyMetric(mt, counter, value, expectedTags));
        }

        [Fact]
        public void TrackInstrument_DimensionValues_SkipsInvalid()
        {
            // arrange
            double value = Random.Shared.NextDouble();
            using Meter meter = new("test.meter.skipinvalid");
            Counter<long> counter = meter.CreateCounter<long>("test.metric.skipinvalid");
            KeyValuePair<string, object?>[] tags =
            [
                new("valid1", "A"),
                new("empty", string.Empty),
                new("space", "   "),
                new("nullval", null),
                new("valid2", "B"),
            ];

            // act
            _client.TrackInstrument(counter, value, tags);
            _client.Flush();

            // assert (only valid1, valid2 should be present)
            MetricIdentifier identifier = new(meter.Name, counter.Name, "valid1", "valid2");
            AIMetric metric = _client.GetMetric(identifier);
            bool exists = metric.TryGetDataSeries(out MetricSeries series, false, ["A", "B"]);

            metric.SeriesCount.Should().Be(2);
            exists.Should().BeTrue();
            series.Should().NotBeNull();

            Dictionary<string, string> expectedTags = new()
            {
                ["valid1"] = "A",
                ["valid2"] = "B",
            };

            _items.Should().ContainSingle()
                .Which.Should().Satisfy<MetricTelemetry>(mt => VerifyMetric(mt, counter, value, expectedTags));
        }

        [Fact]
        public void TrackInstrument_Over10Dimensions_Truncates()
        {
            // arrange
            double value = Random.Shared.NextDouble();
            using Meter meter = new("test.meter.truncate");
            Counter<long> counter = meter.CreateCounter<long>("test.metric.truncate");
            List<KeyValuePair<string, object?>> tags = [];
            for (int i = 0; i < 12; i++)
            {
                tags.Add(new KeyValuePair<string, object?>("dim" + i, "v" + i));
            }

            // act
            _client.TrackInstrument(counter, value, tags.ToArray());
            _client.Flush();

            // assert (only first 10 dimensions)
            MetricIdentifier identifier = new(
                meter.Name,
                counter.Name,
                "dim0", "dim1", "dim2", "dim3", "dim4", "dim5", "dim6", "dim7", "dim8", "dim9");
            AIMetric metric = _client.GetMetric(identifier);
            string[] values = ["v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9"];
            bool exists = metric.TryGetDataSeries(out MetricSeries series, false, values);

            metric.SeriesCount.Should().Be(2);
            exists.Should().BeTrue();
            series.Should().NotBeNull();

            Dictionary<string, string> expectedTags = tags.Take(10).ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString()!);
            _items.Should().ContainSingle()
                .Which.Should().Satisfy<MetricTelemetry>(mt => VerifyMetric(mt, counter, value, expectedTags));
        }

        private static void VerifyMetric(
            MetricTelemetry mt, Instrument instrument, double value, Dictionary<string, string>? tags)
        {
            tags ??= [];
            mt.Should().NotBeNull();
            mt.Name.Should().Be(instrument.Name);
            mt.MetricNamespace.Should().Be(instrument.Meter.Name);
            mt.Count.Should().Be(1);
            mt.Sum.Should().Be(value);

            mt.Properties.Remove("_MS.AggregationIntervalMs");
            mt.Properties.Should().BeEquivalentTo(tags);
        }
    }
}
