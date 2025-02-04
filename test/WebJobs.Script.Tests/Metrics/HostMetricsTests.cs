// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    [Trait(TestTraits.Group, TestTraits.FlexConsumptionMetricsTests)]
    public class HostMetricsTests
    {
        private readonly IServiceProvider _serviceProvider;
        private TestLogger<HostMetrics> _logger;

        public HostMetricsTests()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMetrics();
            serviceCollection.AddFakeLogging();
            serviceCollection.AddSingleton<IEnvironment>(new TestEnvironment());

            _logger = new TestLogger<HostMetrics>();

            // Register HostMetrics with Scoped lifetime and provide the logger
            serviceCollection.AddScoped<IHostMetrics>(provider =>
            {
                return new HostMetrics(provider.GetRequiredService<IMeterFactory>(), provider.GetRequiredService<IEnvironment>(), _logger);
            });

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

        [Fact]
        public void FunctionGroupTag_Set_AfterEnvironmentVariableIsUpdated()
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var meterFactory = _serviceProvider.GetRequiredService<IMeterFactory>();
            var environment = _serviceProvider.GetRequiredService<IEnvironment>();
            var collector = new MetricCollector<long>(meterFactory, HostMetrics.MeterName, HostMetrics.StartedInvocationCount);

            // Act
            metrics.IncrementStartedInvocationCount();
            var measurements = collector.GetMeasurementSnapshot();
            Assert.True(measurements[0].Tags.TryGetValue(OpenTelemetryConstants.AzureFunctionsGroup, out var funcGroup));
            Assert.Equal(string.Empty, funcGroup);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsTargetGroup, "function:test");
            metrics.IncrementStartedInvocationCount();

            // Assert
            measurements = collector.GetMeasurementSnapshot();
            Assert.True(measurements[1].Tags.TryGetValue(OpenTelemetryConstants.AzureFunctionsGroup, out funcGroup));
            Assert.Equal("function:test", funcGroup);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FunctionGroupTag_IsNullOrEmpty_UnableToResolveDebugLog_OnlyOnFlex(bool isFlexSku)
        {
            // Arrange
            var metrics = _serviceProvider.GetRequiredService<IHostMetrics>();
            var meterFactory = _serviceProvider.GetRequiredService<IMeterFactory>();
            var environment = _serviceProvider.GetRequiredService<IEnvironment>();
            var collector = new MetricCollector<long>(meterFactory, HostMetrics.MeterName, HostMetrics.StartedInvocationCount);

            if (isFlexSku)
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);
            }

            // Act
            metrics.IncrementStartedInvocationCount();

            // Assert
            var logs = _logger.GetLogMessages();

            if (isFlexSku)
            {
                var log = logs.Single();
                Assert.Equal(LogLevel.Debug, log.Level);
                Assert.Equal($"Unable to resolve FunctionGroupTag, {EnvironmentSettingNames.FunctionsTargetGroup} is null or empty.", log.FormattedMessage);
            }
            else
            {
                Assert.Empty(logs);
            }
        }
    }
}
