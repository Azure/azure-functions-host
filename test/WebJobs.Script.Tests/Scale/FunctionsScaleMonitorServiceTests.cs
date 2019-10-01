// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class FunctionsScaleMonitorServiceTests
    {
        private readonly FunctionsScaleMonitorService _monitor;
        private readonly Mock<IScaleMonitorManager> _monitorManagerMock;
        private readonly TestMetricsRepository _metricsRepository;
        private readonly Mock<IPrimaryHostStateProvider> _primaryHostStateProviderMock;
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly List<IScaleMonitor> _monitors;
        private bool _isPrimaryHost;

        public FunctionsScaleMonitorServiceTests()
        {
            _isPrimaryHost = true;
            _monitors = new List<IScaleMonitor>();

            _monitorManagerMock = new Mock<IScaleMonitorManager>(MockBehavior.Strict);
            _monitorManagerMock.Setup(p => p.GetMonitors()).Returns(() => _monitors);
            _metricsRepository = new TestMetricsRepository();
            _primaryHostStateProviderMock = new Mock<IPrimaryHostStateProvider>(MockBehavior.Strict);
            _primaryHostStateProviderMock.SetupGet(p => p.IsPrimary).Returns(() => _isPrimaryHost);
            _environment = new TestEnvironment();
            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            var scaleOptions = new ScaleOptions(TimeSpan.FromMilliseconds(50));
            var options = new OptionsWrapper<ScaleOptions>(scaleOptions);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled, "1");

            _monitor = new FunctionsScaleMonitorService(_monitorManagerMock.Object, _metricsRepository, _primaryHostStateProviderMock.Object, _environment, loggerFactory, options);
        }

        [Fact]
        public async Task OnTimer_ExceptionsAreHandled()
        {
            var monitor = new TestScaleMonitor1
            {
                Exception = new Exception("Kaboom!")
            };
            _monitors.Add(monitor);

            await _monitor.StartAsync(CancellationToken.None);

            // wait for a few failures to happen
            LogMessage[] logs = null;
            await TestHelpers.Await(() =>
            {
                logs = _loggerProvider.GetAllLogMessages().Where(p => p.Level == LogLevel.Error).ToArray();
                return logs.Length >= 3;
            });

            Assert.All(logs,
                p =>
                {
                    Assert.Same(monitor.Exception, p.Exception);
                    Assert.Equal("Failed to collect scale metrics sample for monitor 'testscalemonitor1'.", p.FormattedMessage);
                });
        }

        [Fact]
        public async Task StartAsync_RuntimeScaleMonitoringNotEnabled_DoesNotStart()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled, "0");

            await _monitor.StartAsync(CancellationToken.None);

            Assert.Empty(_loggerProvider.GetAllLogMessages());
        }

        [Fact]
        public async Task OnTimer_DoesNotSample_WhenNotPrimaryHost()
        {
            _isPrimaryHost = false;

            var monitor = new TestScaleMonitor1();
            _monitors.Add(monitor);

            await _monitor.StartAsync(CancellationToken.None);

            await Task.Delay(100);

            var logs = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(1, logs.Length);
            Assert.Equal("Runtime scale monitoring is enabled.", logs[0].FormattedMessage);
        }

        [Fact]
        public async Task OnTimer_PersistsMetrics()
        {
            var testMetrics = new List<TestScaleMetrics1>
            {
                new TestScaleMetrics1 { Count = 10 },
                new TestScaleMetrics1 { Count = 15 },
                new TestScaleMetrics1 { Count = 45 },
                new TestScaleMetrics1 { Count = 50 },
                new TestScaleMetrics1 { Count = 100 }
            };
            var monitor1 = new TestScaleMonitor1
            {
                Metrics = testMetrics
            };
            _monitors.Add(monitor1);

            await _monitor.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() =>
            {
                return _metricsRepository.Count >= 5;
            });

            var logs = _loggerProvider.GetAllLogMessages().ToArray();

            var infoLogs = logs.Where(p => p.Level == LogLevel.Information);
            Assert.Equal("Runtime scale monitoring is enabled.", logs[0].FormattedMessage);
            Assert.Equal("Taking metrics samples for 1 monitor(s).", logs[1].FormattedMessage);
            Assert.True(logs[2].FormattedMessage.StartsWith("Scale metrics sample for monitor 'testscalemonitor1': {\"Count\":10,"));

            var metricsWritten = _metricsRepository.Metrics[monitor1].Take(5);
            Assert.Equal(testMetrics, metricsWritten);
        }

        [Fact]
        public async Task OnTimer_MonitorFailuresAreHandled()
        {
            var testMetrics1 = new List<TestScaleMetrics1>
            {
                new TestScaleMetrics1 { Count = 10 },
                new TestScaleMetrics1 { Count = 15 },
                new TestScaleMetrics1 { Count = 45 },
                new TestScaleMetrics1 { Count = 50 },
                new TestScaleMetrics1 { Count = 100 }
            };
            var monitor1 = new TestScaleMonitor1
            {
                Exception = new Exception("Kaboom!")
            };
            _monitors.Add(monitor1);

            var testMetrics2 = new List<TestScaleMetrics2>
            {
                new TestScaleMetrics2 { Num = 300 },
                new TestScaleMetrics2 { Num = 350 },
                new TestScaleMetrics2 { Num = 400 },
                new TestScaleMetrics2 { Num = 450 },
                new TestScaleMetrics2 { Num = 500 }
            };
            var monitor2 = new TestScaleMonitor2
            {
                Metrics = testMetrics2
            };
            _monitors.Add(monitor2);

            await _monitor.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() =>
            {
                return _metricsRepository.Count >= 5;
            });

            var logs = _loggerProvider.GetAllLogMessages().ToArray();

            var infoLogs = logs.Where(p => p.Level == LogLevel.Information);
            Assert.Equal("Runtime scale monitoring is enabled.", logs[0].FormattedMessage);
            Assert.Equal("Taking metrics samples for 2 monitor(s).", logs[1].FormattedMessage);

            // verify the failure logs for the failing monitor
            Assert.True(logs.Count(p => p.FormattedMessage.Equals($"Failed to collect scale metrics sample for monitor 'testscalemonitor1'.")) >= 5);

            // verify each successful sample is logged
            Assert.True(logs.Count(p => p.FormattedMessage.StartsWith($"Scale metrics sample for monitor 'testscalemonitor2'")) >= 5);

            var metricsWritten = _metricsRepository.Metrics[monitor2].Take(5);
            Assert.Equal(testMetrics2, metricsWritten);
        }
    }

    public class TestMetricsRepository : IScaleMetricsRepository
    {
        private int _count;

        public TestMetricsRepository()
        {
            _count = 0;
            Metrics = new Dictionary<IScaleMonitor, IList<ScaleMetrics>>();
        }

        public int Count => _count;

        public IDictionary<IScaleMonitor, IList<ScaleMetrics>> Metrics { get; set; }

        public Task<IDictionary<IScaleMonitor, IList<ScaleMetrics>>> ReadMetricsAsync(IEnumerable<IScaleMonitor> monitors)
        {
            return Task.FromResult<IDictionary<IScaleMonitor, IList<ScaleMetrics>>>(Metrics);
        }

        public Task WriteMetricsAsync(IDictionary<IScaleMonitor, ScaleMetrics> monitorMetrics)
        {
            foreach (var pair in monitorMetrics)
            {
                if (!Metrics.ContainsKey(pair.Key))
                {
                    Metrics[pair.Key] = new List<ScaleMetrics>();
                }

                Metrics[pair.Key].Add(pair.Value);

                Interlocked.Increment(ref _count);
            }

            return Task.CompletedTask;
        }
    }
}
