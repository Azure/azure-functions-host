// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics;

public sealed partial class FlexConsumptionMetricsPublisher
{
    private readonly Dictionary<string, WorkerActivity> _workers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkerInvocation> _workerInvocations = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void WorkerLinked(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (!_options.UseWorkerAttribution)
        {
            return;
        }

        lock (_lock)
        {
            _workers.TryAdd(workerId, new WorkerActivity(_timeProvider.GetUtcNow().UtcDateTime));
        }
    }

    /// <inheritdoc />
    public void WorkerUnlinked(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        lock (_lock)
        {
            if (!_workers.TryGetValue(workerId, out WorkerActivity worker) || worker.UnlinkedAt is not null)
            {
                return;
            }

            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            worker.UnlinkedAt = now;
            foreach (string invocationId in _workerInvocations
                .Where(pair => ReferenceEquals(pair.Value.Worker, worker)).Select(pair => pair.Key).ToArray())
            {
                CompleteWorkerInvocation(invocationId, now, aborted: false);
            }
        }
    }

    /// <inheritdoc />
    public bool TryDispatchInvocation(string invocationId, string workerId, CancellationToken cancellationToken, Func<bool> dispatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(dispatch);
        lock (_lock)
        {
            if (!IsStarted || cancellationToken.IsCancellationRequested ||
                !_workers.TryGetValue(workerId, out WorkerActivity worker) || worker.UnlinkedAt is not null ||
                _workerInvocations.ContainsKey(invocationId))
            {
                _logger.LogError("Cannot attribute invocation {InvocationId} to live Worker {WorkerId}.", invocationId, workerId);
                return false;
            }

            WorkerInvocation invocation = new(worker);
            _workerInvocations.Add(invocationId, invocation);
            if (worker.ActiveInvocationCount++ == 0)
            {
                worker.ActivityIntervalStart = _timeProvider.GetUtcNow().UtcDateTime;
            }

            worker.StartedInvocationCount++;
            try
            {
                if (!dispatch())
                {
                    CompleteWorkerInvocation(invocationId, _timeProvider.GetUtcNow().UtcDateTime, aborted: true);
                    return false;
                }
            }
            catch
            {
                CompleteWorkerInvocation(invocationId, _timeProvider.GetUtcNow().UtcDateTime, aborted: true);
                throw;
            }

            if (!_workerInvocations.ContainsKey(invocationId))
            {
                return true;
            }

            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                lock (_lock)
                {
                    CompleteWorkerInvocation(invocationId, _timeProvider.GetUtcNow().UtcDateTime, aborted: false);
                }
            });

            // Register can synchronously invoke the callback for a cancellation racing dispatch.
            if (!_workerInvocations.ContainsKey(invocationId))
            {
                registration.Unregister();
                return true;
            }

            invocation.CancellationRegistration = registration;

            return true;
        }
    }

    private bool CompleteWorkerInvocation(string invocationId, DateTime now, bool aborted)
    {
        if (!_workerInvocations.Remove(invocationId, out WorkerInvocation invocation))
        {
            return false;
        }

        invocation.CancellationRegistration.Unregister();
        WorkerActivity worker = invocation.Worker;
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
        }

        return true;
    }

    private void MeterWorkerActivity(WorkerActivity worker, DateTime now)
    {
        var (durationMS, highWatermark) = MeasureActivityInterval(worker.ActivityIntervalStart, worker.ActivityIntervalHighWatermark, now);
        worker.ExecutionTimeMS += durationMS;
        worker.ActivityIntervalHighWatermark = highWatermark;
    }

    private async Task PublishWorkerMetricsAsync(DateTime now)
    {
        List<Metrics> snapshots = [];
        lock (_lock)
        {
            foreach ((string workerId, WorkerActivity worker) in _workers.ToArray())
            {
                DateTime intervalEnd = worker.UnlinkedAt ?? now;
                if (worker.ActiveInvocationCount > 0)
                {
                    MeterWorkerActivity(worker, intervalEnd);
                }

                bool hasActivity = worker.ExecutionCount > 0 || worker.ExecutionTimeMS > 0 ||
                    worker.StartedInvocationCount > 0 || worker.ActiveInvocationCount > 0;
                bool keepAlive = (now - worker.LastPublishTime).TotalMilliseconds >= _options.KeepAliveIntervalMS;
                if (hasActivity || keepAlive || IsAlwaysReady)
                {
                    snapshots.Add(new Metrics
                    {
                        InstanceId = workerId,
                        FunctionGroup = _metricsProvider.FunctionGroup,
                        ExecutionCount = worker.ExecutionCount,
                        ExecutionTimeMS = worker.ExecutionTimeMS,
                        StartedInvocationCount = worker.StartedInvocationCount,
                        ActiveInvocationCount = worker.ActiveInvocationCount,
                        TotalTimeMS = Math.Max(0, (long)(intervalEnd - worker.IntervalStart).TotalMilliseconds),
                        IsAlwaysReady = IsAlwaysReady
                    });
                    worker.ExecutionCount = worker.ExecutionTimeMS = worker.StartedInvocationCount = 0;
                    worker.LastPublishTime = now;
                }

                worker.IntervalStart = intervalEnd;
                if (worker.UnlinkedAt is not null)
                {
                    _workers.Remove(workerId);
                }
            }

            // Host meter counters identify the Runtime. Never replicate them into Worker billing records.
            var hostMetrics = _metricsProvider.GetHostMetricsOrNull();
            if (hostMetrics is not null && hostMetrics.TryGetValue(HostMetrics.AppFailureCount, out long failures) && failures > 0)
            {
                _logger.LogWarning("Not attributing {AppFailureCount} Runtime app failures to a Worker.", failures);
            }
        }

        foreach (Metrics metrics in snapshots)
        {
            await _metricsFileManager.PublishMetricsAsync(metrics, metrics.InstanceId);
        }
    }

    private sealed class WorkerActivity(DateTime linkedAt)
    {
        public DateTime IntervalStart { get; set; } = linkedAt;

        public DateTime LastPublishTime { get; set; } = linkedAt;

        public DateTime? UnlinkedAt { get; set; }

        public DateTime ActivityIntervalStart { get; set; }

        public DateTime ActivityIntervalHighWatermark { get; set; }

        public long ActiveInvocationCount { get; set; }

        public long StartedInvocationCount { get; set; }

        public long ExecutionCount { get; set; }

        public long ExecutionTimeMS { get; set; }
    }

    private sealed class WorkerInvocation(WorkerActivity worker)
    {
        public WorkerActivity Worker { get; } = worker;

        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }
}
