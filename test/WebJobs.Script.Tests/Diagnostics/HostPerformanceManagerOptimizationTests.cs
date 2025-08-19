// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class HostPerformanceManagerOptimizationTests
    {
        private readonly HostPerformanceManager _performanceManager;

        public HostPerformanceManagerOptimizationTests()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            var options = new HostHealthMonitorOptions();
            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku)).Returns(ScriptConstants.DynamicSku);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.RoleInstanceId)).Returns((string)null);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAppCountersName)).Returns((string)null);

            _performanceManager = new HostPerformanceManager(
                mockEnvironment.Object, 
                new OptionsWrapper<HostHealthMonitorOptions>(options), 
                serviceProviderMock.Object);
        }

        [Fact]
        public void PerformanceCounterThresholdsExceeded_WithoutCollection_ReturnsExpectedResult()
        {
            var counters = new ApplicationPerformanceCounters
            {
                ActiveConnections = 90,
                ActiveConnectionLimit = 100,
                Connections = 150,
                ConnectionLimit = 200,
                Threads = 400,
                ThreadLimit = 500
            };

            // Test without collection (fast path)
            bool exceededWithoutCollection = HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, null, 0.8f);
            
            // Test with collection (traditional path)
            var exceededCounters = new Collection<string>();
            bool exceededWithCollection = HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters, 0.8f);

            // Both should return the same result
            Assert.Equal(exceededWithoutCollection, exceededWithCollection);
            Assert.True(exceededWithoutCollection); // Should be true because ActiveConnections (90/100 = 0.9) > 0.8
        }

        [Fact]
        public void PerformanceCounterThresholdsExceeded_BelowThreshold_ReturnsFalse()
        {
            var counters = new ApplicationPerformanceCounters
            {
                ActiveConnections = 70,
                ActiveConnectionLimit = 100,
                Connections = 100,
                ConnectionLimit = 200,
                Threads = 300,
                ThreadLimit = 500
            };

            // Test without collection (fast path)
            bool exceededWithoutCollection = HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, null, 0.8f);
            
            // Test with collection (traditional path)
            var exceededCounters = new Collection<string>();
            bool exceededWithCollection = HostPerformanceManager.PerformanceCounterThresholdsExceeded(counters, exceededCounters, 0.8f);

            // Both should return false
            Assert.False(exceededWithoutCollection);
            Assert.False(exceededWithCollection);
            Assert.Empty(exceededCounters);
        }

        [Fact]
        public void IsThresholdExceeded_PerformanceTests()
        {
            // Test edge cases for the optimized IsThresholdExceeded method
            
            // Zero limit should not exceed
            Assert.False(HostPerformanceManager.ThresholdExceeded("test", 100, 0, 0.8f));
            
            // Negative limit should not exceed  
            Assert.False(HostPerformanceManager.ThresholdExceeded("test", 100, -1, 0.8f));
            
            // Exactly at threshold should not exceed
            Assert.False(HostPerformanceManager.ThresholdExceeded("test", 80, 100, 0.8f));
            
            // Just above threshold should exceed
            Assert.True(HostPerformanceManager.ThresholdExceeded("test", 81, 100, 0.8f));
        }
    }
}