// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Drawing.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class TelemetryHealthCheckPublisherTests
    {
        public static TheoryData<TelemetryHealthCheckPublisherOptions, List<CheckDescriptor>, int, string, LogLevel, double> PublishAsyncArgs => new()
        {
            {
                CreateOptions(false, null),
                [HealthStatus.Healthy],
                1,
                "Process reporting healthy: Healthy.",
                LogLevel.Debug,
                1
            },
            {
                CreateOptions(true, null),
                [HealthStatus.Degraded],
                1,
                @"Process reporting unhealthy: Degraded. Health check entries are " +
                @"{""id0"":{""status"":""Degraded"",""description"":""desc0""}}",
                LogLevel.Warning,
                0.5
            },
            {
                CreateOptions(false, null),
                [HealthStatus.Unhealthy],
                1,
                "Process reporting unhealthy: Unhealthy. Health check entries are "
                + @"{""id0"":{""status"":""Unhealthy"",""description"":""desc0""}}",
                LogLevel.Warning,
                0
            },
            {
                CreateOptions(true, null),
                [HealthStatus.Healthy, (HealthStatus.Healthy, "some.tag")],
                0,
                "Process reporting healthy: Healthy.",
                LogLevel.Debug,
                1
            },
            {
                CreateOptions(true, null),
                [HealthStatus.Healthy, HealthStatus.Unhealthy],
                1,
                "Process reporting unhealthy: Unhealthy. Health check entries are "
                + @"{""id0"":{""status"":""Healthy"",""description"":""desc0""},""id1"":{""status"":""Unhealthy"",""description"":""desc1""}}",
                LogLevel.Warning,
                0
            },
            {
                CreateOptions(true, null),
                [HealthStatus.Healthy, (HealthStatus.Unhealthy, "some.tag")],
                1,
                "Process reporting unhealthy: Unhealthy. Health check entries are " +
                @"{""id0"":{""status"":""Healthy"",""description"":""desc0""},""id1"":{""status"":""Unhealthy"",""description"":""desc1""}}",
                LogLevel.Warning,
                0
            },
            {
                CreateOptions(false, null),
                [HealthStatus.Healthy, (HealthStatus.Degraded, "some.tag"), HealthStatus.Unhealthy],
                1,
                "Process reporting unhealthy: Unhealthy. Health check entries are " +
                @"{""id0"":{""status"":""Healthy"",""description"":""desc0""},""id1"":{""status"":""Degraded"",""description"":""desc1""},""id2"":{""status"":""Unhealthy"",""description"":""desc2""}}",
                LogLevel.Warning,
                0
            },
            {
                CreateOptions(false, "some.tag"),
                [(HealthStatus.Healthy, "some.tag"), HealthStatus.Degraded, HealthStatus.Unhealthy, (HealthStatus.Unhealthy, "some.other.tag")],
                1,
                "Process reporting healthy: Healthy.",
                LogLevel.Debug,
                1
            },
            {
                CreateOptions(false, "some.tag"),
                [HealthStatus.Healthy, (HealthStatus.Degraded, "some.tag"), HealthStatus.Unhealthy],
                1,
                "Process reporting unhealthy: Degraded. Health check entries are " +
                @"{""id1"":{""status"":""Degraded"",""description"":""desc1""}}",
                LogLevel.Warning,
                0.5
            },
            {
                CreateOptions(false, "some.tag"),
                [(HealthStatus.Healthy, "some.tag"), (HealthStatus.Degraded, "some.tag"), (HealthStatus.Unhealthy, "some.other.tag"), (HealthStatus.Unhealthy, "some.tag")],
                1,
                "Process reporting unhealthy: Unhealthy. Health check entries are " +
                @"{""id0"":{""status"":""Healthy"",""description"":""desc0""},""id1"":{""status"":""Degraded"",""description"":""desc1""},""id3"":{""status"":""Unhealthy"",""description"":""desc3""}}",
                LogLevel.Warning,
                0
            }
        };

        [Fact]
        public void Ctor_ThrowsWhenOptionsValueNull()
        {
            using Meter meter = new(nameof(Ctor_ThrowsWhenOptionsValueNull));
            HealthCheckMetrics metrics = GetMockedMetrics(meter);
            FakeLogger<TelemetryHealthCheckPublisher> logger = new();

            TestHelpers.Act(() => new TelemetryHealthCheckPublisher(metrics, null, logger))
                .Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Theory]
        [MemberData(nameof(PublishAsyncArgs))]
        public async Task Publish_WritesMetricsAndLogs(
            TelemetryHealthCheckPublisherOptions options,
            IList<CheckDescriptor> checks,
            int expectedLogCount,
            string expectedLogMessage,
            LogLevel expectedLogLevel,
            double expectedMetricValue)
        {
            using Meter meter = new(nameof(Publish_WritesMetricsAndLogs));
            HealthCheckMetrics metrics = GetMockedMetrics(meter);
            using MetricCollector<double> healthyMetricCollector = new(
                meter, HealthCheckMetrics.Constants.ReportMetricName);
            using MetricCollector<double> unhealthyMetricCollector = new(
                meter, HealthCheckMetrics.Constants.UnhealthyMetricName);

            expectedLogMessage = $"[Tag='{options.Tag}'] {expectedLogMessage}";

            FakeLogger<TelemetryHealthCheckPublisher> logger = new();
            FakeLogCollector collector = logger.Collector;

            TelemetryHealthCheckPublisher publisher = new(metrics, options, logger);

            await publisher.PublishAsync(CreateHealthReport(checks), CancellationToken.None);

            collector.Count.Should().Be(expectedLogCount);
            if (expectedLogCount > 0)
            {
                collector.LatestRecord.Message.Should().Be(expectedLogMessage);
                collector.LatestRecord.Level.Should().Be(expectedLogLevel);
            }

            CollectedMeasurement<double> latest = healthyMetricCollector.LastMeasurement;

            latest.Should().NotBeNull();
            latest.Value.Should().Be(expectedMetricValue);

            if (string.IsNullOrWhiteSpace(options.Tag))
            {
                latest.Tags.Should().NotContainKey(HealthCheckMetrics.Constants.HealthCheckTagTag);
            }
            else
            {
                latest.Tags.Should().ContainKey(HealthCheckMetrics.Constants.HealthCheckTagTag)
                    .WhoseValue.Should().Be(options.Tag);
            }

            IReadOnlyList<CollectedMeasurement<double>> unhealthyCounters = unhealthyMetricCollector
                .GetMeasurementSnapshot();

            for (int i = 0; i < checks.Count; i++)
            {
                CheckDescriptor check = checks[i];
                double? value = GetValue(unhealthyCounters, GetKey(i), options.Tag ?? string.Empty);
                if (check.Status == HealthStatus.Healthy)
                {
                    // If the check is healthy, we should not have a value for it
                    value.Should().BeNull();
                }
                else if (options.Tag != null && options.Tag != check.Tag)
                {
                    // If the tag is set and does not match, we should not have a value for this check
                    value.Should().BeNull();
                }
                else
                {
                    // Otherwise, we should have a value for the check
                    value.Should().Be(ToMetricValue(check.Status));
                }
            }
        }

        [Fact]
        public async Task Publish_NoMatchOnTags_NoTelemetry()
        {
            using Meter meter = new(nameof(Publish_NoMatchOnTags_NoTelemetry));
            HealthCheckMetrics metrics = GetMockedMetrics(meter);
            using MetricCollector<double> healthyMetricCollector = new(
                meter, HealthCheckMetrics.Constants.ReportMetricName);
            using MetricCollector<double> unhealthyMetricCollector = new(
                meter, HealthCheckMetrics.Constants.UnhealthyMetricName);
            FakeLogger<TelemetryHealthCheckPublisher> logger = new();

            TelemetryHealthCheckPublisherOptions options = CreateOptions(false, "nonexistent.tag");
            TelemetryHealthCheckPublisher publisher = new(metrics, options, logger);

            List<CheckDescriptor> checks = [HealthStatus.Healthy, (HealthStatus.Unhealthy, "some.tag")];
            await publisher.PublishAsync(CreateHealthReport(checks), CancellationToken.None);

            logger.Collector.Count.Should().Be(0);
            healthyMetricCollector.GetMeasurementSnapshot().Should().BeEmpty();
            unhealthyMetricCollector.GetMeasurementSnapshot().Should().BeEmpty();
        }

        private static TelemetryHealthCheckPublisherOptions CreateOptions(bool logOnlyUnhealthy, string tag)
        {
            return new TelemetryHealthCheckPublisherOptions
            {
                LogOnlyUnhealthy = logOnlyUnhealthy,
                Tag = tag
            };
        }

        private static double? GetValue(
            IReadOnlyCollection<CollectedMeasurement<double>> measurements, string name, string tag)
        {
            static bool MatchTag(CollectedMeasurement<double> measurement, string tag, string value)
            {
                if (measurement.Tags.TryGetValue(tag, out object actual))
                {
                    return actual?.ToString() == value;
                }

                return string.IsNullOrWhiteSpace(value);
            }

            foreach (CollectedMeasurement<double> measurement in measurements)
            {
                if (MatchTag(measurement, HealthCheckMetrics.Constants.HealthCheckNameTag, name) &&
                    MatchTag(measurement, HealthCheckMetrics.Constants.HealthCheckTagTag, tag))
                {
                    return measurement.Value;
                }
            }

            return null;
        }

        private static HealthReport CreateHealthReport(IEnumerable<CheckDescriptor> checks)
        {
            Dictionary<string, HealthReportEntry> healthStatusRecords = [];

            int index = 0;
            foreach (CheckDescriptor check in checks)
            {
                IEnumerable<string> tags = check.Tag is null ? [] : [check.Tag];
                HealthReportEntry entry = new(check.Status, $"desc{index}", TimeSpan.Zero, null, null, tags);
                healthStatusRecords.Add(GetKey(index), entry);
                index++;
            }

            return new HealthReport(healthStatusRecords, TimeSpan.Zero);
        }

        private static string GetKey(int index) => $"id{index}";

        private static HealthCheckMetrics GetMockedMetrics(Meter meter)
        {
            Mock<IMeterFactory> meterFactoryMock = new();
            meterFactoryMock.Setup(x => x.Create(It.IsAny<MeterOptions>())).Returns(meter);
            return new HealthCheckMetrics(meterFactoryMock.Object);
        }

        private static double ToMetricValue(HealthStatus status)
            => status switch
            {
                HealthStatus.Unhealthy => 0,
                HealthStatus.Degraded => 0.5,
                HealthStatus.Healthy => 1,
                _ => throw new NotSupportedException($"Unexpected HealthStatus value: {status}"),
            };

        public record CheckDescriptor(HealthStatus Status, string Tag)
        {
            public static implicit operator CheckDescriptor(HealthStatus status) => new(status, null);

            public static implicit operator CheckDescriptor((HealthStatus Status, string Tag) tuple) => new(tuple.Status, tuple.Tag);
        }
    }
}
