// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    [Trait(TestTraits.Group, TestTraits.HostMetricsTests)]
    public class HostMetricsProviderTests
    {
        private IServiceProvider _serviceProvider;
        private StandbyOptions _standbyOptions;
        private TestOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private TestLogger<HostMetricsProvider> _logger;

        public HostMetricsProviderTests()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMetrics();
            serviceCollection.AddSingleton<IEnvironment>(new TestEnvironment());
            serviceCollection.AddSingleton<IHostMetrics, HostMetrics>();

            // Mock the function activity status provider to return a single outstanding invocation
            var mockStatusProvider = new Mock<IFunctionActivityStatusProvider>();
            var activityStatus = new FunctionActivityStatus() { OutstandingInvocations = 1 };
            mockStatusProvider.Setup(p => p.GetStatus()).Returns(activityStatus);
            serviceCollection.AddSingleton<IFunctionActivityStatusProvider>(mockStatusProvider.Object);

            // Mock the script host manager to return the mock status provider
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(IFunctionActivityStatusProvider))).Returns(mockStatusProvider.Object);
            serviceCollection.AddSingleton<IScriptHostManager>(scriptHostManagerMock.Object);

            _serviceProvider = serviceCollection.BuildServiceProvider();
        }

        private HostMetricsProvider CreateProvider(bool inStandbyMode = false)
        {
            _standbyOptions = new StandbyOptions { InStandbyMode = inStandbyMode };
            _standbyOptionsMonitor = new TestOptionsMonitor<StandbyOptions>(_standbyOptions);
            _logger = new TestLogger<HostMetricsProvider>();
            return new HostMetricsProvider(_serviceProvider, _standbyOptionsMonitor, _logger);
        }

        [Fact]
        public void ProviderStartsOnSpecialization()
        {
            var publisher = CreateProvider(inStandbyMode: true);

            var logs = _logger.GetLogMessages();
            var log = logs.Single();
            Assert.Equal(LogLevel.Debug, log.Level);
            Assert.Equal("Registering StandbyOptions change subscription.", log.FormattedMessage);

            _standbyOptions.InStandbyMode = false;
            _standbyOptionsMonitor.InvokeChanged();

            logs = _logger.GetLogMessages();
            log = logs.Single(p => p.FormattedMessage == "Starting host metrics provider.");
            Assert.NotNull(log);
        }

        [Fact]
        public void GetHostMetricsOrNull_MetricsCaptured_ReturnsHostMetrics()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var hostMetricsProvider = CreateProvider();

            // Act
            metrics.IncrementStartedInvocationCount();
            metrics.AppFailure();
            metrics.AppFailure();

            // Assert
            var result = hostMetricsProvider.GetHostMetricsOrNull();
            result.TryGetValue(HostMetrics.StartedInvocationCount, out var startedInvocationCount);
            result.TryGetValue(HostMetrics.AppFailureCount, out var appFailureCount);
            Assert.Equal(1, startedInvocationCount);
            Assert.Equal(2, appFailureCount);
        }

        [Fact]
        public void GetHostMetricsOrNull_NoMetricsCaptured_ReturnsNull()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var hostMetricsProvider = CreateProvider();

            // Assert
            var result = hostMetricsProvider.GetHostMetricsOrNull();
            Assert.Null(result);
        }

        [Fact]
        public void GetHostMetricsOrNull_PurgesMetricsCache()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var hostMetricsProvider = CreateProvider();

            // Act
            metrics.AppFailure();

            // Assert
            Assert.True(hostMetricsProvider.HasMetrics());
            hostMetricsProvider.GetHostMetricsOrNull();
            Assert.False(hostMetricsProvider.HasMetrics());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void HasMetrics_ReturnsExpectedResult(bool hasMetrics)
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var hostMetricsProvider = CreateProvider();

            // Act
            if (hasMetrics)
            {
                metrics.AppFailure();
            }

            // Assert
            Assert.Equal(hasMetrics, hostMetricsProvider.HasMetrics());
        }

        [Fact]
        public void HostMetricsProvider_StandbyMode_DoesNotStart()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var hostMetricsProvider = CreateProvider(true);

            // Assert
            var result = hostMetricsProvider.GetHostMetricsOrNull();
            Assert.Null(result);
        }
    }
}
