// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class DynamicHealthCheckServiceTests
    {
        private readonly ILogger<DynamicHealthCheckService> _logger = NullLogger<DynamicHealthCheckService>.Instance;
        private readonly Mock<HealthCheckService> _mockWebHostCheck;
        private readonly Mock<IScriptHostManager> _mockManager;
        private readonly Mock<IServiceProvider> _mockServices;

        public DynamicHealthCheckServiceTests()
        {
            _mockWebHostCheck = new Mock<HealthCheckService>();
            _mockManager = new Mock<IScriptHostManager>();
            _mockServices = new Mock<IServiceProvider>();
            _mockManager.Setup(m => m.Services).Returns(_mockServices.Object);
        }

        [Fact]
        public void Constructor_ValidParameters_Success()
        {
            DynamicHealthCheckService service = new(_mockWebHostCheck.Object, _mockManager.Object, _logger);

            service.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_NullWebHostCheck_ThrowsArgumentNullException()
        {
            Action act = () => new DynamicHealthCheckService(null, _mockManager.Object, _logger);

            act.Should().Throw<ArgumentNullException>().WithParameterName("webHostCheck");
        }

        [Fact]
        public void Constructor_NullManager_ThrowsArgumentNullException()
        {
            Action act = () => new DynamicHealthCheckService(_mockWebHostCheck.Object, null, _logger);

            act.Should().Throw<ArgumentNullException>().WithParameterName("manager");
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            Action act = () => new DynamicHealthCheckService(_mockWebHostCheck.Object, _mockManager.Object, null);

            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Theory]
        [InlineData(HealthStatus.Healthy, HealthStatus.Healthy, HealthStatus.Healthy)]
        [InlineData(HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Degraded)]
        [InlineData(HealthStatus.Healthy, HealthStatus.Unhealthy, HealthStatus.Unhealthy)]
        [InlineData(HealthStatus.Degraded, HealthStatus.Healthy, HealthStatus.Degraded)]
        [InlineData(HealthStatus.Degraded, HealthStatus.Degraded, HealthStatus.Degraded)]
        [InlineData(HealthStatus.Degraded, HealthStatus.Unhealthy, HealthStatus.Unhealthy)]
        [InlineData(HealthStatus.Unhealthy, HealthStatus.Healthy, HealthStatus.Unhealthy)]
        [InlineData(HealthStatus.Unhealthy, HealthStatus.Degraded, HealthStatus.Unhealthy)]
        [InlineData(HealthStatus.Unhealthy, HealthStatus.Unhealthy, HealthStatus.Unhealthy)]
        public async Task CheckHealthAsync_BothReportsAvailable_MergesCorrectly(
            HealthStatus webHostStatus,
            HealthStatus scriptHostStatus,
            HealthStatus expectedStatus)
        {
            HealthReport webHostReport = CreateReport("webhost-check", webHostStatus, TimeSpan.FromMilliseconds(100));
            HealthReport scriptHostReport = CreateReport("scripthost-check", scriptHostStatus, TimeSpan.FromMilliseconds(200));
            SetupWebHostHealthService(webHostReport);
            SetupScriptHostHealthService(scriptHostReport);
            DynamicHealthCheckService service = new(_mockWebHostCheck.Object, _mockManager.Object, _logger);

            HealthReport result = await service.CheckHealthAsync();

            result.Should().NotBeNull();
            result.Status.Should().Be(expectedStatus);
            result.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(200)); // Takes the longest duration
            result.Entries.Should().HaveCount(2);
            result.Entries.Should().ContainKey("webhost-check");
            result.Entries.Should().ContainKey("scripthost-check");
        }

        [Fact]
        public async Task CheckHealthAsync_NoScriptHostHealthService_ReturnsWebHostReportOnly()
        {
            HealthReport webHostReport = CreateReport("webhost-check", HealthStatus.Healthy);
            SetupWebHostHealthService(webHostReport);
            _mockServices.Setup(s => s.GetService(typeof(HealthCheckService))).Returns((HealthCheckService)null);
            DynamicHealthCheckService service = new(_mockWebHostCheck.Object, _mockManager.Object, _logger);

            HealthReport result = await service.CheckHealthAsync(_ => true);

            result.Should().BeSameAs(webHostReport);
        }

        [Fact]
        public async Task CheckHealthAsync_DuplicateHealthCheckEntries_KeepsFirst()
        {
            HealthReport webHostReport = CreateReport("duplicate-check", HealthStatus.Healthy, "WebHost check");
            HealthReport scriptHostReport = CreateReport("duplicate-check", HealthStatus.Unhealthy, "ScriptHost check");
            SetupWebHostHealthService(webHostReport);
            SetupScriptHostHealthService(scriptHostReport);

            DynamicHealthCheckService service = new(_mockWebHostCheck.Object, _mockManager.Object, _logger);

            HealthReport result = await service.CheckHealthAsync(_ => true);

            result.Entries.Should().ContainSingle();
            result.Entries["duplicate-check"].Description.Should().Be("WebHost check"); // Should keep the first (left) entry
        }

        [Fact]
        public async Task CheckHealthAsync_PassesArguments()
        {
            using CancellationTokenSource cts = new();
            CancellationToken cancellationToken = cts.Token;
            Func<HealthCheckRegistration, bool> predicate = _ => true;

            HealthReport webHostReport = CreateReport("webhost-check", HealthStatus.Healthy);
            HealthReport scriptHostReport = CreateReport("scripthost-check", HealthStatus.Healthy);

            Mock<HealthCheckService> mockScriptHostHealthService = new();
            mockScriptHostHealthService.Setup(s => s.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), cancellationToken))
                .ReturnsAsync(scriptHostReport);

            _mockServices.Setup(s => s.GetService(typeof(HealthCheckService)))
                .Returns(mockScriptHostHealthService.Object);

            _mockWebHostCheck.Setup(w => w.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), cancellationToken))
                .ReturnsAsync(webHostReport);

            DynamicHealthCheckService service = new(_mockWebHostCheck.Object, _mockManager.Object, _logger);

            await service.CheckHealthAsync(predicate, cancellationToken);

            _mockWebHostCheck.Verify(w => w.CheckHealthAsync(predicate, cancellationToken), Times.Once);
            mockScriptHostHealthService.Verify(s => s.CheckHealthAsync(predicate, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_ScriptHostReportIsNull_ReturnsWebHostReport()
        {
            HealthReport webHostReport = CreateReport("webhost-check", HealthStatus.Healthy);
            SetupWebHostHealthService(webHostReport);
            SetupScriptHostHealthService(null);
            DynamicHealthCheckService service = new(_mockWebHostCheck.Object, _mockManager.Object, _logger);

            HealthReport result = await service.CheckHealthAsync(_ => true);

            result.Should().BeSameAs(webHostReport);
        }

        private static HealthReport CreateReport(
            string entry, HealthStatus status, string description = null)
            => CreateReport(entry, status, TimeSpan.FromMilliseconds(100), description);

        private static HealthReport CreateReport(
            string entry, HealthStatus status, TimeSpan duration, string description = null)
        {
            description ??= $"{entry} is {status}";
            Dictionary<string, HealthReportEntry> entries = new()
            {
                [entry] = new HealthReportEntry(status, description, duration, null, null)
            };

            return new HealthReport(entries, status, duration);
        }

        private void SetupWebHostHealthService(HealthReport report)
        {
            _mockWebHostCheck
                .Setup(s => s.CheckHealthAsync(
                    It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(report);
        }

        private void SetupScriptHostHealthService(HealthReport report)
        {
            Mock<HealthCheckService> mockScriptHostHealthService = new();
            mockScriptHostHealthService
                .Setup(s => s.CheckHealthAsync(
                    It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(report);
            _mockServices.Setup(s => s.GetService(typeof(HealthCheckService)))
                .Returns(mockScriptHostHealthService.Object);
        }

        // Demonstrates the extension-owned connectivity model (Network Troubleshooter): an extension
        // registers a connectivity IHealthCheck in the ScriptHost (JobHost) DI scope—exactly as its
        // IWebJobsStartup.Configure would via services.AddHealthChecks().AddCheck(...)—and the check
        // flows through the WebHost/ScriptHost merge and the /admin/health/connectivity tag filter,
        // with no host dependency on the extension's SDK.
        [Fact]
        public async Task CheckHealthAsync_ExtensionRegistersConnectivityCheckInScriptHostScope_FlowsThroughConnectivityFilter()
        {
            // WebHost scope: a host-owned connectivity check plus a non-connectivity (liveness) check.
            ServiceCollection webHostServices = new();
            webHostServices.AddLogging();
            webHostServices.AddHealthChecks()
                .AddCheck<FakeHostConnectivityCheck>("host.dns.connectivity", tags: [HealthCheckTags.Connectivity])
                .AddCheck<FakeLivenessCheck>("host.liveness", tags: [HealthCheckTags.Liveness]);
            using ServiceProvider webHostProvider = webHostServices.BuildServiceProvider();
            HealthCheckService webHostHealth = webHostProvider.GetRequiredService<HealthCheckService>();

            // ScriptHost scope: an extension registers its own connectivity check.
            ServiceCollection scriptHostServices = new();
            scriptHostServices.AddLogging();
            scriptHostServices.AddHealthChecks()
                .AddCheck<FakeExtensionConnectivityCheck>("ext.eventhubs.connectivity", tags: [HealthCheckTags.Connectivity]);
            using ServiceProvider scriptHostProvider = scriptHostServices.BuildServiceProvider();
            _mockManager.Setup(m => m.Services).Returns(scriptHostProvider);

            DynamicHealthCheckService service = new(webHostHealth, _mockManager.Object, _logger);

            // Query with the same predicate the /admin/health/connectivity route uses.
            HealthReport result = await service.CheckHealthAsync(r => r.Tags.Contains(HealthCheckTags.Connectivity));

            // Both connectivity checks flow through the merge; the non-connectivity check is filtered out.
            result.Entries.Should().ContainKey("host.dns.connectivity");
            result.Entries.Should().ContainKey("ext.eventhubs.connectivity");
            result.Entries.Should().NotContainKey("host.liveness");
        }

        private sealed class FakeHostConnectivityCheck : IHealthCheck
        {
            public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
                => Task.FromResult(HealthCheckResult.Healthy());
        }

        private sealed class FakeExtensionConnectivityCheck : IHealthCheck
        {
            public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
                => Task.FromResult(HealthCheckResult.Healthy());
        }

        private sealed class FakeLivenessCheck : IHealthCheck
        {
            public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
                => Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}