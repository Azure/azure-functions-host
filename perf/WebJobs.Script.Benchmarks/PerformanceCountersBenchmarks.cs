// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;
using Microsoft.Azure.WebJobs.Script.Scale;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class PerformanceCountersBenchmarks
    {
        private ApplicationPerformanceCounters _counters;
        private readonly float _threshold = 0.8f;

        [GlobalSetup]
        public void Setup()
        {
            _counters = new ApplicationPerformanceCounters
            {
                ActiveConnections = 80,
                ActiveConnectionLimit = 100,
                Connections = 150,
                ConnectionLimit = 200,
                Threads = 400,
                ThreadLimit = 500,
                Processes = 8,
                ProcessLimit = 10,
                NamedPipes = 40,
                NamedPipeLimit = 50,
                Sections = 120,
                SectionLimit = 150,
                RemoteDirMonitors = 24,
                RemoteDirMonitorLimit = 30
            };
        }

        [Benchmark(Baseline = true)]
        public bool CheckThresholds_WithCollection()
        {
            var exceededCounters = new Collection<string>();
            return HostPerformanceManager.PerformanceCounterThresholdsExceeded(_counters, exceededCounters, _threshold);
        }

        [Benchmark]
        public bool CheckThresholds_WithoutCollection()
        {
            return HostPerformanceManager.PerformanceCounterThresholdsExceeded(_counters, null, _threshold);
        }

        [Benchmark]
        public bool CheckSingleThreshold_ActiveConnections()
        {
            return HostPerformanceManager.ThresholdExceeded("ActiveConnections", _counters.ActiveConnections, _counters.ActiveConnectionLimit, _threshold);
        }

        [Benchmark]
        public bool CheckSingleThreshold_Connections()
        {
            return HostPerformanceManager.ThresholdExceeded("Connections", _counters.Connections, _counters.ConnectionLimit, _threshold);
        }
    }
}