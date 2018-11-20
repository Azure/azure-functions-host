// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class HostPerformanceManagerTests
    {
        [Fact]
        public void GetCounters_ReturnsExpectedResult()
        {
            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            string value = string.Empty;
            var logger = new TestLogger("Test");
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAppCountersName)).Returns(() => value);
            var options = new HostHealthMonitorOptions();
            var performanceManager = new HostPerformanceManager(mockEnvironment.Object, new OptionsWrapper<HostHealthMonitorOptions>(options));

            value = "{\"userTime\": 30000000,\"kernelTime\": 16562500,\"pageFaults\": 131522,\"processes\": 1,\"processLimit\": 32,\"threads\": 32,\"threadLimit\": 512,\"connections\": 4,\"connectionLimit\": 300,\"activeConnections\": 25,\"activeConnectionLimit\": 600,\"sections\": 3,\"sectionLimit\": 256,\"namedPipes\": 0,\"namedPipeLimit\": 128,\"remoteDirMonitors\": 5,\"remoteDirMonitorLimit\": 500,\"readIoOperations\": 675,\"writeIoOperations\": 18,\"otherIoOperations\": 9721,\"readIoBytes\": 72585119,\"writeIoBytes\": 5446,\"otherIoBytes\": 393926,\"privateBytes\": 33759232,\"handles\": 987,\"contextSwitches\": 15535,\"remoteOpens\": 250}";
            var counters = performanceManager.GetPerformanceCounters(logger);
            Assert.Equal(counters.PageFaults, 131522);

            // verify garbage characters are trimmed
            value = value + "猅";
            counters = performanceManager.GetPerformanceCounters(logger);
            Assert.Equal(counters.PageFaults, 131522);

            value = "{}";
            counters = performanceManager.GetPerformanceCounters(logger);
            Assert.Equal(counters.PageFaults, 0);

            value = "this is not json";
            counters = performanceManager.GetPerformanceCounters(logger);
            Assert.Null(counters);
            var error = logger.GetLogMessages().Last();
            Assert.Equal("Failed to deserialize application performance counters. JSON Content: \"this is not json\"", error.FormattedMessage);
        }

        [Theory]
        [InlineData(0, 100, 0.60F, false)]
        [InlineData(60, 100, 0.60F, false)]
        [InlineData(61, 100, 0.60F, true)]
        [InlineData(100, 100, 0.60F, true)]
        [InlineData(101, 100, 0.60F, true)]
        [InlineData(101, 0, 0.60F, false)]
        [InlineData(101, -1, 0.60F, false)]
        public void ThresholdExceeded_ReturnsExpectedValue(int currentValue, int limit, float threshold, bool expected)
        {
            Assert.Equal(expected, HostPerformanceManager.ThresholdExceeded("Test", currentValue, limit, threshold));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(240, false)]
        [InlineData(241, true)]
        [InlineData(290, true)]
        [InlineData(300, true)]
        [InlineData(310, true)]
        public void IsUnderHighLoad_Connections_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                Connections = currentValue,
                ConnectionLimit = 300
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("Connections", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(480, false)]
        [InlineData(481, true)]
        [InlineData(500, true)]
        [InlineData(600, true)]
        [InlineData(610, true)]
        public void IsUnderHighLoad_ActiveConnections_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                ActiveConnections = currentValue,
                ActiveConnectionLimit = 600
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("ActiveConnections", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(400, false)]
        [InlineData(401, true)]
        [InlineData(500, true)]
        [InlineData(600, true)]
        public void IsUnderHighLoad_RemoteDirMonitors_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                RemoteDirMonitors = currentValue,
                RemoteDirMonitorLimit = 500
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("RemoteDirMonitors", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(409, false)]
        [InlineData(410, true)]
        [InlineData(500, true)]
        [InlineData(512, true)]
        [InlineData(513, true)]
        public void IsUnderHighLoad_Threads_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                Threads = currentValue,
                ThreadLimit = 512
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("Threads", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(25, false)]
        [InlineData(26, true)]
        [InlineData(30, true)]
        [InlineData(32, true)]
        [InlineData(33, true)]
        public void IsUnderHighLoad_Processes_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                Processes = currentValue,
                ProcessLimit = 32
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("Processes", exceededCounters[0]);
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(10, false)]
        [InlineData(102, false)]
        [InlineData(103, true)]
        [InlineData(120, true)]
        [InlineData(128, true)]
        [InlineData(129, true)]
        public void IsUnderHighLoad_NamedPipes_ReturnsExpectedResults(int currentValue, bool expected)
        {
            var counters = new ApplicationPerformanceCounters
            {
                NamedPipes = currentValue,
                NamedPipeLimit = 128
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(expected, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters: exceededCounters));
            if (expected)
            {
                Assert.Equal(1, exceededCounters.Count);
                Assert.Equal("NamedPipes", exceededCounters[0]);
            }
        }

        [Fact]
        public void IsUnderHighLoad_MultipleExceededThrottles_ReturnsExpectedResults()
        {
            var counters = new ApplicationPerformanceCounters
            {
                NamedPipes = 130,
                NamedPipeLimit = 128,
                Processes = 40,
                ProcessLimit = 32,
                Threads = 600,
                ThreadLimit = 512
            };
            Collection<string> exceededCounters = new Collection<string>();
            Assert.Equal(true, HostPerformanceManager.IsUnderHighLoad(counters, exceededCounters));
            Assert.Equal(3, exceededCounters.Count);
            Assert.Equal("Threads, Processes, NamedPipes", string.Join(", ", exceededCounters));
        }
    }
}
