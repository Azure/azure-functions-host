// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Scale;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class ProcessMonitorTests
    {
        private readonly TestEnvironment _env;
        private readonly ProcessMonitor _monitor;
        private List<TimeSpan> _testProcessorTimeValues;

        public ProcessMonitorTests()
        {
            _testProcessorTimeValues = new List<TimeSpan>();
            _env = new TestEnvironment();
            _env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            _monitor = new ProcessMonitor(new TestProcessMetricsProvider(_testProcessorTimeValues), _env);
        }

        public static IEnumerable<object[]> CPULoadTestData =>
            new List<object[]>
            {
                new object[]
                {
                    new List<double> { 0, 250, 750, 1500, 2500, 3300, 3900, 4600, 5100, 5500, 5800 },
                    new List<double> { 25, 50, 75, 100, 80, 60, 70, 50, 40, 30 }
                },
                new object[]
                {
                    new List<double> { 100, 300, 500, 700, 1000, 1500, 2000, 3000, 3850, 4800, 5600, 6300, 7000, 7500, 7800, 8000 },
                    new List<double> { 50, 100, 85, 95, 80, 70, 70, 50, 30, 20 }
                }
            };

        [Fact]
        public async Task Start_StartsSampleTimer()
        {
            int intervalMS = 10;
            var localMonitor = new ProcessMonitor(Process.GetCurrentProcess(), _env, TimeSpan.FromMilliseconds(intervalMS));

            var stats = localMonitor.GetStats();
            Assert.Equal(0, stats.CpuLoadHistory.Count());

            localMonitor.Start();

            // wait long enough for enough samples to be taken to verify sample history rolling
            await Task.Delay(2 * ProcessMonitor.SampleHistorySize * intervalMS);

            stats = localMonitor.GetStats();
            Assert.True(stats.CpuLoadHistory.Count() == ProcessMonitor.SampleHistorySize);
        }

        [Theory]
        [MemberData(nameof(CPULoadTestData))]
        public void SampleCPULoad_AccumulatesSamples(List<double> testProcessorTimeValues, List<double> expectedLoadValues)
        {
            _testProcessorTimeValues.AddRange(testProcessorTimeValues.Select(p => TimeSpan.FromMilliseconds(p)));

            var stats = _monitor.GetStats();
            Assert.Equal(0, stats.CpuLoadHistory.Count());

            // start taking samples, using a constant duration so our expected
            // calculations are deterministic
            var sampleDuration = TimeSpan.FromSeconds(1);
            for (int i = 0; i < _testProcessorTimeValues.Count; i++)
            {
                _monitor.SampleCPULoad(sampleDuration);
            }

            stats = _monitor.GetStats();

            // expect a max of 10 - old samples are removed
            var cpuLoadResults = stats.CpuLoadHistory.ToList();
            Assert.Equal(Math.Min(cpuLoadResults.Count, ProcessMonitor.SampleHistorySize), cpuLoadResults.Count);

            Assert.Equal(expectedLoadValues, cpuLoadResults);
        }

        private class TestProcessMetricsProvider : IProcessMetricsProvider
        {
            private int _idx = 0;
            private List<TimeSpan> _processorTimeValues;

            public TestProcessMetricsProvider(List<TimeSpan> processorTimeValues)
            {
                _processorTimeValues = processorTimeValues;
            }

            public TimeSpan TotalProcessorTime
            {
                get
                {
                    return _processorTimeValues[_idx++];
                }
            }
        }
    }
}
