// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    /// <summary>
    /// Publishes host-wide billing metrics for traditional Flex Consumption.
    /// </summary>
    public sealed class FlexConsumptionMetricsPublisher : FlexConsumptionMetricsPublisherBase
    {
        private DateTime _currentActivityIntervalStart;
        private DateTime _activityIntervalHighWatermark = DateTime.MinValue;
        private DateTime _lastPublishTime = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexConsumptionMetricsPublisher"/> class.
        /// Initializes a publisher that attributes billing to the host instance.
        /// </summary>
        public FlexConsumptionMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions, IOptions<FlexConsumptionMetricsPublisherOptions> options,
            ILogger<FlexConsumptionMetricsPublisher> logger, IFileSystem fileSystem, IHostMetricsProvider metricsProvider, TimeProvider timeProvider = null)
            : base(environment, standbyOptions, options, logger, fileSystem, metricsProvider, timeProvider)
        {
            Initialize();
        }

        // internal properties for testing
        internal long FunctionExecutionCount { get; set; }

        internal long FunctionExecutionSuccessCount { get; set; }

        internal long FunctionExecutionTimeMS { get; set; }

        internal long ActiveFunctionCount { get; set; }

        private protected override async Task PublishMetricsAsync(DateTime now, ValueStopwatch stopwatch)
        {
            lock (SyncLock)
            {
                if (ActiveFunctionCount > 0)
                {
                    // at the end of an interval, we'll meter any outstanding activity up to the end of the interval
                    MeterCurrentActiveInterval(now);
                }
            }

            bool hasActivity = FunctionExecutionCount > 0 || FunctionExecutionTimeMS > 0 || MetricsProvider.HasMetrics();
            bool shouldForcePublish = (now - _lastPublishTime) >= TimeSpan.FromMilliseconds(PublisherOptions.KeepAliveIntervalMS);

            if (!hasActivity && !shouldForcePublish && !IsAlwaysReady)
            {
                // No activity and not time for keep-alive publish & not always ready
                return;
            }

            // we've been accumulating function activity for the entire period
            // publish this activity and reset
            Metrics metrics = null;
            lock (SyncLock)
            {
                metrics = new Metrics
                {
                    TotalTimeMS = (long)stopwatch.GetElapsedTime().TotalMilliseconds,
                    ExecutionCount = FunctionExecutionCount,
                    ExecutionSuccessCount = FunctionExecutionSuccessCount,
                    ExecutionTimeMS = FunctionExecutionTimeMS,
                    IsAlwaysReady = IsAlwaysReady,
                    InstanceId = MetricsProvider.InstanceId,
                    FunctionGroup = MetricsProvider.FunctionGroup
                };

                var scaleMetrics = MetricsProvider.GetHostMetricsOrNull();
                if (scaleMetrics is not null)
                {
                    metrics.AppFailureCount = scaleMetrics.TryGetValue(HostMetrics.AppFailureCount, out long appFailureCount) ? appFailureCount : 0;
                    metrics.StartedInvocationCount = scaleMetrics.TryGetValue(HostMetrics.StartedInvocationCount, out long startedInvocationCount) ? startedInvocationCount : 0;
                    metrics.ActiveInvocationCount = scaleMetrics.TryGetValue(HostMetrics.ActiveInvocationCount, out long activeInvocationCount) ? activeInvocationCount : 0;
                }

                FunctionExecutionTimeMS = FunctionExecutionCount = FunctionExecutionSuccessCount = 0;
                _lastPublishTime = now;
            }

            await FileManager.PublishMetricsAsync(metrics);
        }

        internal override void OnFunctionStarted(string functionName, string invocationId, DateTime now)
        {
            if (!IsStarted)
            {
                return;
            }

            lock (SyncLock)
            {
                if (ActiveFunctionCount == 0)
                {
                    // we're transitioning from inactive to active
                    _currentActivityIntervalStart = now;
                }

                ActiveFunctionCount++;
            }
        }

        internal override void OnFunctionCompleted(string functionName, string invocationId, DateTime now, bool success = false)
        {
            if (!IsStarted)
            {
                return;
            }

            lock (SyncLock)
            {
                if (ActiveFunctionCount > 0)
                {
                    ActiveFunctionCount--;
                }
                else
                {
                    // We got a completion event without a corresponding start.
                    // This might happen during specialization for example.
                    // Ignore the event.
                    return;
                }

                if (ActiveFunctionCount == 0)
                {
                    // We're transitioning from active to inactive, so we need to accumulate the elapsed time
                    // for this interval.
                    MeterCurrentActiveInterval(now);
                }

                FunctionExecutionCount++;

                if (success)
                {
                    FunctionExecutionSuccessCount++;
                }
            }
        }

        private void MeterCurrentActiveInterval(DateTime now)
        {
            var (durationMS, highWatermark) = MeasureActivityInterval(_currentActivityIntervalStart, _activityIntervalHighWatermark, now);
            FunctionExecutionTimeMS += durationMS;
            _activityIntervalHighWatermark = highWatermark;
        }
    }
}
