// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public class ProcessMonitor : IDisposable
    {
        internal const int SampleHistorySize = 10;
        internal const int DefaultSampleIntervalSeconds = 1;

        private readonly List<double> _cpuLoadHistory = new List<double>();
        private readonly int _effectiveCores;

        private Timer _timer;
        private IProcessMetricsProvider _processMetricsProvider;
        private TimeSpan? _lastProcessorTime;
        private DateTime _lastSampleTime;
        private bool _disposed = false;
        private TimeSpan? _interval;
        private object _syncLock = new object();

        // for mock testing only
        internal ProcessMonitor()
        {
        }

        public ProcessMonitor(Process process, IEnvironment environment, TimeSpan? interval = null)
            : this(new DefaultProcessMetricsProvider(process), environment, interval)
        {
        }

        public ProcessMonitor(IProcessMetricsProvider processMetricsProvider, IEnvironment environment, TimeSpan? interval = null)
        {
            _interval = interval ?? TimeSpan.FromSeconds(DefaultSampleIntervalSeconds);
            _processMetricsProvider = processMetricsProvider;
            _effectiveCores = environment.GetEffectiveCoresCount();
        }

        public virtual void Start()
        {
            _timer = new Timer(OnTimer, null, TimeSpan.Zero, _interval.Value);
        }

        public virtual ProcessStats GetStats()
        {
            ProcessStats stats = null;
            lock (_syncLock)
            {
                stats = new ProcessStats
                {
                    CpuLoadHistory = _cpuLoadHistory.ToArray()
                };
            }
            return stats;
        }

        private void OnTimer(object state)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var currSampleTime = DateTime.UtcNow;
                var currSampleDuration = currSampleTime - _lastSampleTime;

                SampleProcessMetrics(currSampleDuration);

                _lastSampleTime = currSampleTime;
            }
            catch
            {
                // don't allow background exceptions to escape
            }
        }

        internal void SampleProcessMetrics(TimeSpan currSampleDuration)
        {
            SampleCPULoad(currSampleDuration);
        }

        internal void SampleCPULoad(TimeSpan currSampleDuration)
        {
            var currProcessorTime = _processMetricsProvider.TotalProcessorTime;

            if (_lastProcessorTime != null)
            {
                var currSampleProcessorTime = (currProcessorTime - _lastProcessorTime.Value).TotalMilliseconds;
                var totalSampleProcessorTime = _effectiveCores * currSampleDuration.TotalMilliseconds;

                double cpuLoad = currSampleProcessorTime / totalSampleProcessorTime;
                cpuLoad = Math.Round(cpuLoad * 100);

                AddSample(_cpuLoadHistory, cpuLoad);
            }

            _lastProcessorTime = currProcessorTime;
        }

        private void AddSample(List<double> samples, double sample)
        {
            lock (_syncLock)
            {
                if (samples.Count == SampleHistorySize)
                {
                    samples.RemoveAt(0);
                }
                samples.Add(sample);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
