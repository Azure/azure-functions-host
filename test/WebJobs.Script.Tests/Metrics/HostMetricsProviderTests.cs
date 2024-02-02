// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    [Trait(TestTraits.Group, TestTraits.HostMetricsTests)]
    public class HostMetricsProviderTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHostMetricsProvider _hostMetricsProvider;

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
            _hostMetricsProvider = new HostMetricsProvider(_serviceProvider);
        }

        [Fact]
        public void GetHostMetricsOrNull_MetricsCaptured_ReturnsHostMetrics()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();

            // Act
            metrics.IncrementStartedInvocationCount();
            metrics.AppFailure();
            metrics.AppFailure();

            // Assert
            var result = _hostMetricsProvider.GetHostMetricsOrNull();
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

            // Assert
            var result = _hostMetricsProvider.GetHostMetricsOrNull();
            Assert.Null(result);
        }

        [Fact]
        public void GetHostMetricsOrNull_PurgesMetricsCache()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();

            // Act
            metrics.AppFailure();

            // Assert
            Assert.True(_hostMetricsProvider.HasMetrics());
            _hostMetricsProvider.GetHostMetricsOrNull();
            Assert.False(_hostMetricsProvider.HasMetrics());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void HasMetrics_ReturnsExpectedResult(bool hasMetrics)
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();

            // Act
            if (hasMetrics)
            {
                metrics.AppFailure();
            }

            // Assert
            Assert.Equal(hasMetrics, _hostMetricsProvider.HasMetrics());
        }
    }
}
