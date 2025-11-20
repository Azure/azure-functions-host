// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class ApplicationInsightsMetricExporterOptionsTests
    {
        [Fact]
        public void ShouldListenTo_NullInstrument_Throws()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            Instrument? instrument = null;

            // act
            Action act = () => options.ShouldListenTo(instrument!);

            // assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("instrument");
        }

        [Fact]
        public void ShouldListenTo_Empty_ReturnsFalse()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            using Meter meter = new("test.meter");
            Counter<long> counter = meter.CreateCounter<long>("test.counter");

            // act
            bool result = options.ShouldListenTo(counter);

            // assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldListenTo_MeterNotSet_ReturnsFalse()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            options.Meters.Add("configured.meter");
            using Meter meter = new("different.meter");
            Counter<long> counter = meter.CreateCounter<long>("test.counter");

            // act
            bool result = options.ShouldListenTo(counter);

            // assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldListenTo_MeterSet_ReturnsTrue()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            options.Meters.Add("test.meter");
            using Meter meter = new("test.meter");
            Counter<long> counter = meter.CreateCounter<long>("test.counter");

            // act
            bool result = options.ShouldListenTo(counter);

            // assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldListenTo_IsCaseSensitive()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            options.Meters.Add("Test.Meter");
            using Meter meter = new("test.meter"); // different case
            Counter<long> counter = meter.CreateCounter<long>("test.counter");

            // act
            bool result = options.ShouldListenTo(counter);

            // assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldListenTo_WorksWithDifferentInstrumentTypes()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            options.Meters.Add("test.meter");
            using Meter meter = new("test.meter");
            Counter<long> counter = meter.CreateCounter<long>("test.counter");
            Histogram<double> histogram = meter.CreateHistogram<double>("test.histogram");
            ObservableGauge<int> gauge = meter.CreateObservableGauge<int>("test.gauge", () => 1);

            // act & assert
            options.ShouldListenTo(counter).Should().BeTrue();
            options.ShouldListenTo(histogram).Should().BeTrue();
            options.ShouldListenTo(gauge).Should().BeTrue();
        }

        [Fact]
        public void ShouldListenTo_HandlesMultipleConfiguredMeters()
        {
            // arrange
            ApplicationInsightsMetricExporterOptions options = new();
            options.Meters.Add("meter1");
            options.Meters.Add("meter2");

            using Meter meter1 = new("meter1");
            using Meter meter2 = new("meter2");
            using Meter meter3 = new("meter3");

            Counter<long> counter1 = meter1.CreateCounter<long>("counter1");
            Counter<long> counter2 = meter2.CreateCounter<long>("counter2");
            Counter<long> counter3 = meter3.CreateCounter<long>("counter3");

            // act & assert
            options.ShouldListenTo(counter1).Should().BeTrue();
            options.ShouldListenTo(counter2).Should().BeTrue();
            options.ShouldListenTo(counter3).Should().BeFalse();
        }
    }
}