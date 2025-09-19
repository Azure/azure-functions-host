// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class HealthCheckExtensionsTests
    {
        public static TheoryData<string[], string[]> AddTelemetryPublisherData => new()
        {
            { null, [null] },
            { [null], [null] },
            { [string.Empty], [null] },
            { ["tag1"], [null, "tag1"] },
            { ["tag1", "tag2"], [null, "tag1", "tag2"] },
        };

        [Fact]
        public void Filter_ReturnsFilteredHealthReport()
        {
            const string tag = "test.tag.1";
            static HealthReportEntry CreateEntry(HealthStatus status, string tag)
            {
                return tag == null
                    ? new HealthReportEntry(status, null, TimeSpan.Zero, null, null)
                    : new HealthReportEntry(status, null, TimeSpan.Zero, null, null, [tag]);
            }

            // arrange
            Dictionary<string, HealthReportEntry> entries = new()
            {
                ["test.check.1"] = CreateEntry(HealthStatus.Healthy, null),
                ["test.check.2"] = CreateEntry(HealthStatus.Healthy, tag),
                ["test.check.3"] = CreateEntry(HealthStatus.Unhealthy, tag),
                ["test.check.4"] = CreateEntry(HealthStatus.Healthy, "test.tag.2"),
            };

            HealthReport healthReport = new(entries, TimeSpan.FromSeconds(Random.Shared.Next(0, 10)));

            // act
            HealthReport filteredReport = healthReport.Filter((key, entry) => entry.Tags.Contains(tag));

            // assert
            static void Verify(HealthReportEntry actual, HealthStatus status)
            {
                actual.Status.Should().Be(status);
                actual.Data.Should().BeEmpty();
                actual.Description.Should().BeNull();
                actual.Exception.Should().BeNull();
                actual.Duration.Should().Be(TimeSpan.Zero);
                actual.Tags.Should().Contain(tag);
            }

            filteredReport.Should().NotBeSameAs(healthReport);
            filteredReport.TotalDuration.Should().Be(healthReport.TotalDuration);
            filteredReport.Status.Should().Be(HealthStatus.Unhealthy);
            filteredReport.Entries.Should().HaveCount(2);
            filteredReport.Entries.Should().ContainKey("test.check.2")
                .WhoseValue.Should().Satisfy<HealthReportEntry>(
                    entry => Verify(entry, HealthStatus.Healthy));
            filteredReport.Entries.Should().ContainKey("test.check.3")
                .WhoseValue.Should().Satisfy<HealthReportEntry>(
                    entry => Verify(entry, HealthStatus.Unhealthy));
        }

        [Fact]
        public void Filter_NoMatch_ReturnsEmptyHealthReport()
        {
            const string tag = "test.tag.1";
            static HealthReportEntry CreateEntry(HealthStatus status, string tag)
            {
                return tag == null
                    ? new HealthReportEntry(status, null, TimeSpan.Zero, null, null)
                    : new HealthReportEntry(status, null, TimeSpan.Zero, null, null, [tag]);
            }

            // arrange
            Dictionary<string, HealthReportEntry> entries = new()
            {
                ["test.check.1"] = CreateEntry(HealthStatus.Healthy, null),
                ["test.check.2"] = CreateEntry(HealthStatus.Healthy, tag),
                ["test.check.3"] = CreateEntry(HealthStatus.Unhealthy, tag),
                ["test.check.4"] = CreateEntry(HealthStatus.Healthy, tag),
            };

            HealthReport healthReport = new(entries, TimeSpan.FromSeconds(Random.Shared.Next(0, 10)));

            // act
            HealthReport filteredReport = healthReport.Filter((key, entry) => entry.Tags.Contains("nonexistant.tag"));

            // assert
            filteredReport.Should().NotBeSameAs(healthReport);
            filteredReport.TotalDuration.Should().Be(healthReport.TotalDuration);
            filteredReport.Status.Should().Be(HealthStatus.Healthy);
            filteredReport.Entries.Should().BeEmpty();
        }

        [Fact]
        public void AddTelemetryPublisher_ReturnsOriginalBuilder()
        {
            // arrange
            ServiceCollection services = new();
            HealthChecksBuilder builder = new(services);

            // act
            IHealthChecksBuilder returned = builder.AddTelemetryPublisher();

            // assert
            returned.Should().BeSameAs(builder);
        }

        [Theory]
        [MemberData(nameof(AddTelemetryPublisherData))]
        public void AddTelemetryPublisher_RegistersExpected(string[] tags, string[] expected)
        {
            // arrange
            ServiceCollection services = new();
            HealthChecksBuilder builder = new(services);

            // act
            builder.AddTelemetryPublisher(tags);

            // assert
            services.Where(x => x.ServiceType == typeof(IHealthCheckPublisher)).Should().HaveCount(expected.Length)
                .And.AllSatisfy(x => x.Lifetime.Should().Be(ServiceLifetime.Singleton));

            ServiceProvider provider = services.BuildServiceProvider();
            IEnumerable<IHealthCheckPublisher> publishers = provider.GetServices<IHealthCheckPublisher>();

            publishers.Should().HaveCount(expected.Length);
            foreach (string tag in expected)
            {
                publishers.Should().ContainSingle(p => VerifyPublisher(p, tag));
            }
        }

        private static bool VerifyPublisher(IHealthCheckPublisher publisher, string tag)
        {
            return publisher is TelemetryHealthCheckPublisher telemetryPublisher
                && telemetryPublisher.Tag == tag;
        }

        private class HealthChecksBuilder(IServiceCollection services) : IHealthChecksBuilder
        {
            public IServiceCollection Services { get; } = services;

            public List<HealthCheckRegistration> Registrations { get; } = [];

            public IHealthChecksBuilder Add(HealthCheckRegistration registration)
            {
                Registrations.Add(registration);
                return this;
            }
        }
    }
}
