// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Metrics;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    [Trait(TestTraits.Group, TestTraits.FlexConsumptionMetricsTests)]
    public class HostMetricsTests
    {
        private readonly IServiceProvider _serviceProvider;

        public HostMetricsTests()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMetrics();
            serviceCollection.AddSingleton<IEnvironment>(new TestEnvironment());
            serviceCollection.AddSingleton<IHostMetrics, HostMetrics>();
            _serviceProvider = serviceCollection.BuildServiceProvider();
        }

        [Fact]
        public void AppFailure_Increments_AppFailureCount()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var meterFactory = _serviceProvider.GetRequiredService<IMeterFactory>();
            var collector = new MetricCollector<long>(meterFactory, HostMetrics.MeterName, HostMetrics.AppFailureCount);

            // Act
            metrics.AppFailure();
            metrics.AppFailure();

            // Assert
            var measurements = collector.GetMeasurementSnapshot();
            Assert.Equal(2, measurements.Count);
            Assert.Equal(1, measurements[0].Value);
        }

        [Fact]
        public void IncrementStartedInvocationCount_Increments_StartedInvocationCount()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var meterFactory = _serviceProvider.GetRequiredService<IMeterFactory>();
            var collector = new MetricCollector<long>(meterFactory, HostMetrics.MeterName, HostMetrics.StartedInvocationCount);

            // Act
            metrics.IncrementStartedInvocationCount();

            // Assert
            var measurements = collector.GetMeasurementSnapshot();
            Assert.Equal(1, measurements.Count);
            Assert.Equal(1, measurements[0].Value);
        }
    }
}
