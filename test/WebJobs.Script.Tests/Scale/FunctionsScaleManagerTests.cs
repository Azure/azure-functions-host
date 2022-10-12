// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
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
        private readonly Mock<ITargetScalerManager> _targetScalerManagerMock;
        private readonly Mock<IConcurrencyStatusRepository> _concurrencyStatusRepositoryMock;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly List<IScaleMonitor> _monitors;
        private readonly List<ITargetScaler> _targetScalers;
        private readonly Mock<IFunctionsHostingConfiguration> _functionsHostingConfigurationMock;
        private readonly TestEnvironment _environment;
        private readonly ILogger _testLogger;

        public FunctionsScaleManagerTests()
        {
            _monitors = new List<IScaleMonitor>();
            _targetScalers = new List<ITargetScaler>();
            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _testLogger = loggerFactory.CreateLogger("Test");

            _monitorManagerMock = new Mock<IScaleMonitorManager>(MockBehavior.Strict);
            _monitorManagerMock.Setup(p => p.GetMonitors()).Returns(() => _monitors);
            _metricsRepositoryMock = new Mock<IScaleMetricsRepository>(MockBehavior.Strict);
            _targetScalerManagerMock = new Mock<ITargetScalerManager>(MockBehavior.Strict);
            _targetScalerManagerMock.Setup(p => p.GetTargetScalers()).Returns(() => _targetScalers);
            _concurrencyStatusRepositoryMock = new Mock<IConcurrencyStatusRepository>(MockBehavior.Strict);
            _concurrencyStatusRepositoryMock.Setup(p => p.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new HostConcurrencySnapshot()
                {
                    FunctionSnapshots = new Dictionary<string, FunctionConcurrencySnapshot>()
                    {
                        { "func1", new FunctionConcurrencySnapshot() { Concurrency = 1 } }
                    }
                });

            _functionsHostingConfigurationMock = new Mock<IFunctionsHostingConfiguration>(MockBehavior.Strict);
            _environment = new TestEnvironment();

            _scaleManager = new FunctionsScaleManager(_monitorManagerMock.Object, _metricsRepositoryMock.Object, _targetScalerManagerMock.Object, _concurrencyStatusRepositoryMock.Object, _functionsHostingConfigurationMock.Object, _environment, loggerFactory);
        }

        [Theory]
        [InlineData(0, ScaleVote.None)]
        [InlineData(1, ScaleVote.ScaleIn)]
        public async Task GetScaleStatus_NoMonitors_ReturnsExpectedStatus(int workerCount, ScaleVote expected)
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = workerCount
            };
            var status = await _scaleManager.GetScaleStatusAsync(context);

            Assert.Equal(expected, status.Vote);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GetScaleStatus_ReturnsExpectedResult(bool tbsEnabled)
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = 3
            };

            var monitor1 = new TestScaleMonitor1();
            monitor1.Status = new ScaleStatus
            {
                Vote = ScaleVote.ScaleIn
            };
            var monitor2 = new TestScaleMonitor1();
            monitor2.Status = new ScaleStatus
            {
                Vote = ScaleVote.ScaleOut
            };
            var monitor3 = new TestScaleMonitor1();
            monitor3.Status = new ScaleStatus
            {
                Vote = ScaleVote.ScaleIn
            };

            List<IScaleMonitor> monitors = new List<IScaleMonitor>
            {
                monitor1,
                monitor2,
                monitor3
            };
            _monitorManagerMock.Setup(p => p.GetMonitors()).Returns(monitors);

            var monitorMetrics = new Dictionary<IScaleMonitor, IList<ScaleMetrics>>
            {
                { monitor1, new List<ScaleMetrics>() },
                { monitor2, new List<ScaleMetrics>() },
                { monitor3, new List<ScaleMetrics>() }
            };
            _metricsRepositoryMock.Setup(p => p.ReadMetricsAsync(It.IsAny<IEnumerable<IScaleMonitor>>())).ReturnsAsync(monitorMetrics);

            var targetScaler1 = new TestTargetScaler()
            {
                Result = new TargetScalerResult()
                {
                    TargetWorkerCount = 2
                },
                TargetScalerDescriptor = new TargetScalerDescriptor("func1")
            };

            _targetScalerManagerMock.Setup(p => p.GetTargetScalers()).Returns(new List<ITargetScaler> { targetScaler1 });

            if (tbsEnabled)
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, "1");
            }

            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            _functionsHostingConfigurationMock.Setup(p => p.GetValue(assemblyName, It.IsAny<string>())).Returns("1");

            var status = await _scaleManager.GetScaleStatusAsync(context);

            var logs = _loggerProvider.GetAllLogMessages();
            if (!tbsEnabled)
            {
                Assert.Equal("Computing scale status (WorkerCount=3)", logs[0].FormattedMessage);
                Assert.Equal("3 scale monitors to sample", logs[1].FormattedMessage);
                Assert.Equal("Monitor 'testscalemonitor1' voted 'ScaleIn'", logs[2].FormattedMessage);
                Assert.Equal("Monitor 'testscalemonitor1' voted 'ScaleOut'", logs[3].FormattedMessage);
                Assert.Equal("Monitor 'testscalemonitor1' voted 'ScaleIn'", logs[4].FormattedMessage);
                Assert.Equal(ScaleVote.ScaleOut, status.Vote);
                Assert.Equal(null, status.TargetWorkerCount);
            }
            else
            {
                Assert.Equal("1 target scalers to sample", logs[0].FormattedMessage);
                Assert.Equal("Snapshot dynamic concurrency for target scaler 'func1' is '1'", logs[1].FormattedMessage);
                Assert.Equal("Target worker count for 'func1' is '2'", logs[2].FormattedMessage);
                Assert.Equal(ScaleVote.ScaleIn, status.Vote);
                Assert.Equal(2, status.TargetWorkerCount);
            }
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
            Assert.Equal("Monitor 'testscalemonitor1' voted 'ScaleIn'", logs[2].FormattedMessage);
            Assert.Equal("Failed to query scale status for monitor 'testscalemonitor2'.", logs[3].FormattedMessage);
            Assert.Same(exception, logs[3].Exception);
            Assert.Equal("Monitor 'testscalemonitor3' voted 'ScaleIn'", logs[4].FormattedMessage);

            Assert.Equal(null, status.TargetWorkerCount);
            Assert.Equal(ScaleVote.ScaleIn, status.Vote);
        }

        [Fact]
        public async Task GetScaleStatus_TargetScalerFails_ReturnsExpectedResult()
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = 3
            };

            var targetScaler1 = new TestTargetScaler { Result = new TargetScalerResult { TargetWorkerCount = 3 }, TargetScalerDescriptor = new TargetScalerDescriptor("func1") };
            var targetScaler2 = new TestTargetScaler2 { Result = new TargetScalerResult { TargetWorkerCount = 1 }, TargetScalerDescriptor = new TargetScalerDescriptor("func2") };
            var targetScaler3 = new TestTargetScaler { Result = new TargetScalerResult { TargetWorkerCount = -3 }, TargetScalerDescriptor = new TargetScalerDescriptor("func3") };
            List<ITargetScaler> targetScalers = new List<ITargetScaler>
            {
                targetScaler1,
                targetScaler2,
                targetScaler3
            };
            _targetScalerManagerMock.Setup(p => p.GetTargetScalers()).Returns(targetScalers);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, "1");

            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            _functionsHostingConfigurationMock.Setup(p => p.GetValue(assemblyName, It.IsAny<string>())).Returns("1");

            var status = await _scaleManager.GetScaleStatusAsync(context);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal("3 target scalers to sample", logs[0].FormattedMessage);
            Assert.Equal($"Snapshot dynamic concurrency for target scaler 'func1' is '1'", logs[1].FormattedMessage);
            Assert.Equal("Target worker count for 'func1' is '3'", logs[2].FormattedMessage);
            Assert.Equal("Failed to query scale result for target scaler 'func2'.", logs[3].FormattedMessage);
            Assert.Same("test", logs[3].Exception.Message);
            Assert.Equal("Target worker count for 'func3' is '-3'", logs[4].FormattedMessage);

            Assert.Equal(3, status.TargetWorkerCount);
            Assert.Equal(ScaleVote.None, status.Vote);
        }

        [Theory]
        [InlineData(0, 0, 0, ScaleVote.None)]
        [InlineData(1, 0, 0, ScaleVote.ScaleIn)]
        [InlineData(1, 1, 3, ScaleVote.ScaleOut)]
        [InlineData(0, 0, 1, ScaleVote.None)]
        [InlineData(1, 0, 1, ScaleVote.ScaleIn)]
        [InlineData(5, 0, 3, ScaleVote.ScaleIn)]
        public void GetAggregateScaleVote_ReturnsExpectedResult(int workerCount, int numScaleOutVotes, int numScaleInVotes, ScaleVote expected)
        {
            var context = new ScaleStatusContext
            {
                WorkerCount = workerCount
            };
            List<ScaleVote> votes = new List<ScaleVote>();
            for (int i = 0; i < numScaleOutVotes; i++)
            {
                votes.Add(ScaleVote.ScaleOut);
            }
            for (int i = 0; i < numScaleInVotes; i++)
            {
                votes.Add(ScaleVote.ScaleIn);
            }
            var vote = FunctionsScaleManager.GetAggregateScaleVote(votes, context, _testLogger);
            Assert.Equal(expected, vote);
        }

        [Theory]
        [InlineData(false, true, 1, 0)]
        [InlineData(true, false, 1, 0)]
        [InlineData(true, true, 0, 1)]
        public void GetScaleInstancesToProcess_Returns_Expected(bool targetBaseScalingEnabled, bool triggerEabled, int expectedScaleMonitorCount, int expectedTargetScalerCount)
        {
            List<IScaleMonitor> scaleMonitors = new List<IScaleMonitor>
            {
                new TestScaleMonitor<ScaleMetrics>("func1-test-test"),
            };
            List<ITargetScaler> targetScalers = new List<ITargetScaler>
            {
                new TestTargetScaler()
                {
                    TargetScalerDescriptor = new TargetScalerDescriptor("func1")
                }
            };

            Mock<IFunctionsHostingConfiguration> functionsHostingConfigurationMock = new Mock<IFunctionsHostingConfiguration>(MockBehavior.Strict);
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            functionsHostingConfigurationMock.Setup(p => p.GetValue(assemblyName, It.IsAny<string>())).Returns(triggerEabled ? "1" : null);

            TestEnvironment env = new TestEnvironment();
            if (targetBaseScalingEnabled)
            {
                env.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, "1");
            }

            FunctionsScaleManager.GetScalersToSample(env, functionsHostingConfigurationMock.Object, scaleMonitors, targetScalers,
                out List<IScaleMonitor> scaleMonitorsToProcess, out List<ITargetScaler> targetScalesToProcess);

            Assert.Equal(scaleMonitorsToProcess.Count(), expectedScaleMonitorCount);
            Assert.Equal(targetScalesToProcess.Count(), expectedTargetScalerCount);
        }
    }
}
