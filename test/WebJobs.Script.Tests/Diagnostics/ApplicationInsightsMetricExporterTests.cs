// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public sealed class ApplicationInsightsMetricExporterTests : IDisposable
    {
        private const string InstrumentName = "test.instrument";
        private readonly TelemetryConfiguration _config;
        private readonly List<ITelemetry> _items = [];

        public ApplicationInsightsMetricExporterTests()
        {
            Mock<ITelemetryChannel> mockChannel = new();
            mockChannel.Setup(c => c.Send(It.IsAny<ITelemetry>()))
                .Callback<ITelemetry>(_items.Add);

            _config = new TelemetryConfiguration
            {
                ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                TelemetryChannel = mockChannel.Object
            };
        }

        public static Dictionary<string, Action<Meter>> InstrumentActions { get; } = new()
        {
            ["Counter{byte}"] = (meter) => meter.CreateCounter<byte>(InstrumentName).Add(1),
            ["Counter{short}"] = (meter) => meter.CreateCounter<short>(InstrumentName).Add(1),
            ["Counter{int}"] = (meter) => meter.CreateCounter<int>(InstrumentName).Add(1),
            ["Counter{long}"] = (meter) => meter.CreateCounter<long>(InstrumentName).Add(1),
            ["Counter{float}"] = (meter) => meter.CreateCounter<float>(InstrumentName).Add(1),
            ["Counter{double}"] = (meter) => meter.CreateCounter<double>(InstrumentName).Add(1),
            ["Counter{decimal}"] = (meter) => meter.CreateCounter<decimal>(InstrumentName).Add(1),
            ["Histogram{byte}"] = (meter) => meter.CreateHistogram<byte>(InstrumentName).Record(1),
            ["Histogram{short}"] = (meter) => meter.CreateHistogram<short>(InstrumentName).Record(1),
            ["Histogram{int}"] = (meter) => meter.CreateHistogram<int>(InstrumentName).Record(1),
            ["Histogram{long}"] = (meter) => meter.CreateHistogram<long>(InstrumentName).Record(1),
            ["Histogram{float}"] = (meter) => meter.CreateHistogram<float>(InstrumentName).Record(1),
            ["Histogram{double}"] = (meter) => meter.CreateHistogram<double>(InstrumentName).Record(1),
            ["Histogram{decimal}"] = (meter) => meter.CreateHistogram<decimal>(InstrumentName).Record(1),
        };

        public static IEnumerable<object[]> InstrumentTests => InstrumentActions.Keys.Select(k => new object[] { k });

        public void Dispose()
        {
            _config.Dispose();
        }

        [Fact]
        public void Constructor_ThrowsOnNullOptions()
        {
            // act
            Action act = () => new ApplicationInsightsMetricExporter(null!);

            // assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public void Initialize_ThrowsOnNullConfiguration()
        {
            // arrange
            ApplicationInsightsMetricExporter exporter = CreateExporter();

            // act
            Action act = () => exporter.Initialize(null!);

            // assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        }

        [Fact]
        public void MeterListener_IgnoresInstrumentsNotInConfiguration()
        {
            // arrange
            ApplicationInsightsMetricExporter exporter = CreateExporter("configured.meter");
            exporter.Initialize(_config);

            // act - create instrument from unconfigured meter
            using Meter meter = new("unconfigured.meter");
            Counter<long> counter = meter.CreateCounter<long>("test.counter");
            counter.Add(1);
            exporter.Flush();

            // assert - no telemetry should be sent for unconfigured meters
            _items.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(InstrumentTests))]
        public void MeterListener_TracksConfiguredInstruments(string test)
        {
            // arrange
            ApplicationInsightsMetricExporter exporter = CreateExporter("configured.meter");
            exporter.Initialize(_config);

            // Small delay to ensure initialization completes
            Thread.Sleep(100);

            // act - create and use instrument from configured meter
            using Meter meter = new("configured.meter");
            InstrumentActions[test](meter);
            exporter.Flush();

            // Small delay to allow async processing
            Thread.Sleep(100);

            _items.Should().ContainSingle()
                .Which.Should().Satisfy<MetricTelemetry>(t =>
                {
                    t.Name.Should().Be("test.instrument");
                    t.MetricNamespace.Should().Be("configured.meter");
                    t.Sum.Should().Be(Convert.ToDouble(1));
                });
        }

        private static ApplicationInsightsMetricExporter CreateExporter(params string[] meters)
            => new(CreateOptions(meters));

        private static OptionsWrapper<ApplicationInsightsMetricExporterOptions> CreateOptions(params string[] meters)
        {
            ApplicationInsightsMetricExporterOptions options = new();
            foreach (string meter in meters)
            {
                options.Meters.Add(meter);
            }

            return new(options);
        }
    }
}
