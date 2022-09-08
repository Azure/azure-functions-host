// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class FunctionsScaleManagerTests
    {
        private readonly FunctionsScaleManager _scaleManager;
        private readonly Mock<IScaleMonitorManager> _monitorManagerMock;
        private readonly Mock<IScaleMetricsRepository> _metricsRepositoryMock;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly List<IScaleMonitor> _monitors;
        private readonly ILogger _testLogger;

        public FunctionsScaleManagerTests()
        {
            _monitors = new List<IScaleMonitor>();
            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _testLogger = loggerFactory.CreateLogger("Test");

            _monitorManagerMock = new Mock<IScaleMonitorManager>(MockBehavior.Strict);
            _monitorManagerMock.Setup(p => p.GetMonitors()).Returns(() => _monitors);
            _metricsRepositoryMock = new Mock<IScaleMetricsRepository>(MockBehavior.Strict);

            _scaleManager = new FunctionsScaleManager(_monitorManagerMock.Object, _metricsRepositoryMock.Object, loggerFactory);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, -1)]
        public async Task GetScaleStatus_NoMonitors_ReturnsExpectedStatus(int workerCount, int expected)
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = workerCount
            };
            var status = await _scaleManager.GetScaleStatusAsync(context);

            Assert.Equal(expected, status);
        }

        [Fact]
        public async Task GetScaleStatus_ReturnsExpectedResult()
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = 3
            };

            var mockMonitor1 = new Mock<IScaleMonitor>(MockBehavior.Strict);
            mockMonitor1.Setup(p => p.GetScaleStatus(It.Is<ScaleStatusContext>(q => q.WorkerCount == context.WorkerCount))).Returns(new ScaleStatus { Vote = ScaleVote.ScaleIn });
            mockMonitor1.SetupGet(p => p.Descriptor).Returns(new ScaleMonitorDescriptor("testscalemonitor1"));
            var mockMonitor2 = new Mock<IScaleMonitor>(MockBehavior.Strict);
            mockMonitor2.Setup(p => p.GetScaleStatus(It.Is<ScaleStatusContext>(q => q.WorkerCount == context.WorkerCount))).Returns(new ScaleStatus { Vote = ScaleVote.ScaleOut });
            mockMonitor2.SetupGet(p => p.Descriptor).Returns(new ScaleMonitorDescriptor("testscalemonitor2"));
            var mockMonitor3 = new Mock<IScaleMonitor>(MockBehavior.Strict);
            mockMonitor3.Setup(p => p.GetScaleStatus(It.Is<ScaleStatusContext>(q => q.WorkerCount == context.WorkerCount))).Returns(new ScaleStatus { Vote = ScaleVote.ScaleIn });
            mockMonitor3.SetupGet(p => p.Descriptor).Returns(new ScaleMonitorDescriptor("testscalemonitor3"));
            List<IScaleMonitor> monitors = new List<IScaleMonitor>
            {
                mockMonitor1.Object,
                mockMonitor2.Object,
                mockMonitor3.Object
            };
            _monitorManagerMock.Setup(p => p.GetMonitors()).Returns(monitors);

            var monitorMetrics = new Dictionary<IScaleMonitor, IList<ScaleMetrics>>
            {
                { mockMonitor1.Object, new List<ScaleMetrics>() },
                { mockMonitor2.Object, new List<ScaleMetrics>() },
                { mockMonitor3.Object, new List<ScaleMetrics>() }
            };
            _metricsRepositoryMock.Setup(p => p.ReadMetricsAsync(It.IsAny<IEnumerable<IScaleMonitor>>())).ReturnsAsync(monitorMetrics);

            var status = await _scaleManager.GetScaleStatusAsync(context);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal("Computing scale status (WorkerCount=3)", logs[0].FormattedMessage);
            Assert.Equal("3 scale monitors to sample", logs[1].FormattedMessage);
            Assert.Equal("Monitor 'testscalemonitor1' voted '-1'", logs[2].FormattedMessage);
            Assert.Equal("Monitor 'testscalemonitor2' voted '1'", logs[3].FormattedMessage);
            Assert.Equal("Monitor 'testscalemonitor3' voted '-1'", logs[4].FormattedMessage);

            Assert.Equal(1, status);
        }

        [Fact]
        public async Task GetScaleStatus_MonitorFails_ReturnsExpectedResult()
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = 3
            };

            var mockMonitor1 = new Mock<IScaleMonitor>(MockBehavior.Strict);
            mockMonitor1.Setup(p => p.GetScaleStatus(It.Is<ScaleStatusContext>(q => q.WorkerCount == context.WorkerCount))).Returns(new ScaleStatus { Vote = ScaleVote.ScaleIn });
            mockMonitor1.SetupGet(p => p.Descriptor).Returns(new ScaleMonitorDescriptor("testscalemonitor1"));
            var mockMonitor2 = new Mock<IScaleMonitor>(MockBehavior.Strict);
            mockMonitor2.SetupGet(p => p.Descriptor).Returns(new ScaleMonitorDescriptor("testscalemonitor2"));
            var exception = new Exception("Kaboom!");
            mockMonitor2.Setup(p => p.GetScaleStatus(It.Is<ScaleStatusContext>(q => q.WorkerCount == context.WorkerCount))).Throws(exception);
            var mockMonitor3 = new Mock<IScaleMonitor>(MockBehavior.Strict);
            mockMonitor3.Setup(p => p.GetScaleStatus(It.Is<ScaleStatusContext>(q => q.WorkerCount == context.WorkerCount))).Returns(new ScaleStatus { Vote = ScaleVote.ScaleIn });
            mockMonitor3.SetupGet(p => p.Descriptor).Returns(new ScaleMonitorDescriptor("testscalemonitor3"));
            List<IScaleMonitor> monitors = new List<IScaleMonitor>
            {
                mockMonitor1.Object,
                mockMonitor2.Object,
                mockMonitor3.Object
            };
            _monitorManagerMock.Setup(p => p.GetMonitors()).Returns(monitors);

            var monitorMetrics = new Dictionary<IScaleMonitor, IList<ScaleMetrics>>
            {
                { mockMonitor1.Object, new List<ScaleMetrics>() },
                { mockMonitor2.Object, new List<ScaleMetrics>() },
                { mockMonitor3.Object, new List<ScaleMetrics>() }
            };
            _metricsRepositoryMock.Setup(p => p.ReadMetricsAsync(It.IsAny<IEnumerable<IScaleMonitor>>())).ReturnsAsync(monitorMetrics);

            var status = await _scaleManager.GetScaleStatusAsync(context);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal("Computing scale status (WorkerCount=3)", logs[0].FormattedMessage);
            Assert.Equal("3 scale monitors to sample", logs[1].FormattedMessage);
            Assert.Equal("Monitor 'testscalemonitor1' voted '-1'", logs[2].FormattedMessage);
            Assert.Equal("Failed to query scale status for monitor 'testscalemonitor2'.", logs[3].FormattedMessage);
            Assert.Same(exception, logs[3].Exception);
            Assert.Equal("Monitor 'testscalemonitor3' voted '-1'", logs[4].FormattedMessage);

            Assert.Equal(-1, status);
        }

        [Theory]
        [InlineData(0, new int[] { },  0)]
        [InlineData(1, new int[] { }, -1)]
        [InlineData(1, new int[] { 1, -1, -1, -1 }, 1)]
        [InlineData(0, new int[] { -1 }, 0)]
        [InlineData(1, new int[] { -1 }, -1)]
        [InlineData(5, new int[] { -1, -1, -1 }, -1)]
        [InlineData(2, new int[] { -1, 6, -1 }, 6)]
        [InlineData(3, new int[] { -1, -6, -2 }, -1)]
        [InlineData(0, new int[] { -1, -6, -2 }, 0)]
        public void GetAggregateScaleVote_ReturnsExpectedResult(int workerCount, int[] votes, int expected)
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = workerCount
            };
            var vote = FunctionsScaleManager.GetAggregateScaleVote(new List<int>(votes), context, _testLogger);
            Assert.Equal(expected, vote);
        }
    }
}
