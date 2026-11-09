// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics;

/// <summary>
/// Publishes independently attributed billing metrics for compute-separated Workers.
/// </summary>
public sealed class FlexConsumptionPerWorkerMetricsPublisher : FlexConsumptionMetricsPublisherBase, IWorkerInvocationTracker
{
    private readonly Dictionary<string, WorkerActivity> _workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkerActivity> _workerInvocations = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="FlexConsumptionPerWorkerMetricsPublisher"/> class.
    /// Initializes a publisher that attributes billing to selected Worker channels.
    /// </summary>
    public FlexConsumptionPerWorkerMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions,
        IOptions<FlexConsumptionMetricsPublisherOptions> options, ILogger<FlexConsumptionPerWorkerMetricsPublisher> logger,
        IFileSystem fileSystem, IHostMetricsProvider metricsProvider, TimeProvider timeProvider = null)
        : base(environment, standbyOptions, options, logger, fileSystem, metricsProvider, timeProvider)
    {
        Initialize();
    }

    private protected override void OnStarted(DateTime now)
    {
        foreach (WorkerActivity worker in _workers.Values)
        {
            worker.IntervalStart = now;
            worker.LastPublishTime = now;
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        lock (SyncLock)
        {
            _workerInvocations.Clear();
            _workers.Clear();
        }
    }

    internal override void OnFunctionStarted(string functionName, string invocationId, DateTime now)
    {
        // Host start precedes channel selection and is not Worker execution.
    }

    internal override void OnFunctionCompleted(string functionName, string invocationId, DateTime now, bool success = false)
    {
        // Host completion may precede actual Worker termination after a timeout.
        // The channel result, not this event or a cancellation request, ends Worker billing.
    }

    /// <inheritdoc />
    public void InvocationCompleted(string invocationId, bool success)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        lock (SyncLock)
        {
            if (!CompleteWorkerInvocation(invocationId, Clock.GetUtcNow().UtcDateTime, aborted: false, success))
            {
                Logger.LogWarning("No active Worker attribution for completed invocation {InvocationId}. It may have already completed or lost its Worker. No Runtime billing fallback will be emitted.", invocationId);
            }
        }
    }

    /// <inheritdoc />
    public void WorkerLinked(string workerId, string functionGroup = null, bool? isAlwaysReady = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        lock (SyncLock)
        {
            _workers.TryAdd(workerId, new WorkerActivity(
                Clock.GetUtcNow().UtcDateTime,
                functionGroup,
                isAlwaysReady));
        }
    }

    /// <inheritdoc />
    public void WorkerUnlinked(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        lock (SyncLock)
        {
            if (!_workers.TryGetValue(workerId, out WorkerActivity worker) || worker.UnlinkedAt is not null)
            {
                return;
            }

            DateTime now = Clock.GetUtcNow().UtcDateTime;
            worker.UnlinkedAt = now;
            foreach (string invocationId in _workerInvocations
                .Where(pair => ReferenceEquals(pair.Value, worker)).Select(pair => pair.Key).ToArray())
            {
                CompleteWorkerInvocation(invocationId, now, aborted: false);
            }
        }
    }

    /// <inheritdoc />
    public bool TryDispatchInvocation(string invocationId, string workerId, Func<bool> dispatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(dispatch);
        lock (SyncLock)
        {
            if (!IsStarted ||
                !_workers.TryGetValue(workerId, out WorkerActivity worker) || worker.UnlinkedAt is not null ||
                _workerInvocations.ContainsKey(invocationId))
            {
                Logger.LogError("Cannot attribute invocation {InvocationId} to live Worker {WorkerId}.", invocationId, workerId);
                return false;
            }

            _workerInvocations.Add(invocationId, worker);
            if (worker.ActiveInvocationCount++ == 0)
            {
                worker.ActivityIntervalStart = Clock.GetUtcNow().UtcDateTime;
            }

            worker.StartedInvocationCount++;
            try
            {
                if (!dispatch())
                {
                    CompleteWorkerInvocation(invocationId, Clock.GetUtcNow().UtcDateTime, aborted: true);
                    return false;
                }
            }
            catch
            {
                CompleteWorkerInvocation(invocationId, Clock.GetUtcNow().UtcDateTime, aborted: true);
                throw;
            }

            return true;
        }
    }

    private bool CompleteWorkerInvocation(string invocationId, DateTime now, bool aborted, bool success = false)
    {
        if (!_workerInvocations.Remove(invocationId, out WorkerActivity worker))
        {
            return false;
        }

        worker.ActiveInvocationCount--;
        if (aborted)
        {
            worker.StartedInvocationCount--;
        }
        else
        {
            if (worker.ActiveInvocationCount == 0)
            {
                MeterWorkerActivity(worker, now);
            }

            worker.ExecutionCount++;
            if (success)
            {
                worker.ExecutionSuccessCount++;
            }
        }

        return true;
    }

    private void MeterWorkerActivity(WorkerActivity worker, DateTime now)
    {
        var (durationMS, highWatermark) = MeasureActivityInterval(worker.ActivityIntervalStart, worker.ActivityIntervalHighWatermark, now);
        worker.ExecutionTimeMS += durationMS;
        worker.ActivityIntervalHighWatermark = highWatermark;
    }

    private protected override async Task PublishMetricsAsync(DateTime now, ValueStopwatch stopwatch)
    {
        FileManager.DeleteExpiredWorkerMetrics(now, PublisherOptions.WorkerMetricsFileRetentionPeriod);

        List<Metrics> snapshots = [];
        lock (SyncLock)
        {
            foreach ((string workerId, WorkerActivity worker) in _workers.ToArray())
            {
                DateTime intervalEnd = worker.UnlinkedAt ?? now;
                if (intervalEnd < worker.IntervalStart)
                {
                    // This Worker linked after the publication timestamp was captured.
                    continue;
                }

                if (worker.ActiveInvocationCount > 0)
                {
                    MeterWorkerActivity(worker, intervalEnd);
                }

                bool hasActivity = worker.ExecutionCount > 0 || worker.ExecutionTimeMS > 0 ||
                    worker.StartedInvocationCount > 0 || worker.ActiveInvocationCount > 0;
                bool keepAlive = (now - worker.LastPublishTime).TotalMilliseconds >= PublisherOptions.KeepAliveIntervalMS;
                bool isAlwaysReady = worker.IsAlwaysReady ?? IsAlwaysReady;
                if (hasActivity || keepAlive || isAlwaysReady)
                {
                    snapshots.Add(new Metrics
                    {
                        InstanceId = workerId,
                        FunctionGroup = string.IsNullOrEmpty(worker.FunctionGroup)
                            ? MetricsProvider.FunctionGroup
                            : worker.FunctionGroup,
                        ExecutionCount = worker.ExecutionCount,
                        ExecutionSuccessCount = worker.ExecutionSuccessCount,
                        ExecutionTimeMS = worker.ExecutionTimeMS,
                        StartedInvocationCount = worker.StartedInvocationCount,
                        ActiveInvocationCount = worker.ActiveInvocationCount,
                        TotalTimeMS = Math.Max(0, (long)(intervalEnd - worker.IntervalStart).TotalMilliseconds),
                        IsAlwaysReady = isAlwaysReady
                    });
                    worker.ExecutionCount = worker.ExecutionSuccessCount = worker.ExecutionTimeMS = worker.StartedInvocationCount = 0;
                    worker.LastPublishTime = now;
                }

                worker.IntervalStart = intervalEnd;
                if (worker.UnlinkedAt is not null)
                {
                    _workers.Remove(workerId);
                }
            }

            // Host meter counters identify the Runtime. Never replicate them into Worker billing records.
            var hostMetrics = MetricsProvider.GetHostMetricsOrNull();
            if (hostMetrics is not null && hostMetrics.TryGetValue(HostMetrics.AppFailureCount, out long failures) && failures > 0)
            {
                Logger.LogWarning("Not attributing {AppFailureCount} Runtime app failures to a Worker.", failures);
            }
        }

        foreach (Metrics metrics in snapshots)
        {
            await FileManager.PublishMetricsAsync(metrics, metrics.InstanceId);
        }
    }

    private sealed class WorkerActivity(DateTime linkedAt, string functionGroup, bool? isAlwaysReady)
    {
        public string FunctionGroup { get; } = functionGroup;

        public bool? IsAlwaysReady { get; } = isAlwaysReady;

        public DateTime IntervalStart { get; set; } = linkedAt;

        public DateTime LastPublishTime { get; set; } = linkedAt;

        public DateTime? UnlinkedAt { get; set; }

        public DateTime ActivityIntervalStart { get; set; }

        public DateTime ActivityIntervalHighWatermark { get; set; }

        public long ActiveInvocationCount { get; set; }

        public long StartedInvocationCount { get; set; }

        public long ExecutionCount { get; set; }

        public long ExecutionSuccessCount { get; set; }

        public long ExecutionTimeMS { get; set; }
    }
}
