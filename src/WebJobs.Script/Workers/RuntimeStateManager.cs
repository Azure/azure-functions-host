// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Default <see cref="IRuntimeStateManager"/> implementation. Thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Two independent lifecycles are tracked:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Linked set</b> — drives <see cref="RuntimeState.LinkedWorkerCount"/>.
///   A worker is linked from <see cref="OnWorkerLinked"/> until
///   <see cref="OnWorkerUnlinked"/>, regardless of health.
///   </description></item>
///   <item><description>
///   <b>Capacity contributions</b> — drive <see cref="RuntimeState.TotalRequestSlots"/>.
///   Only counted from <see cref="OnWorkerCapacityAvailable"/> until
///   <see cref="OnWorkerCapacityUnavailable"/>, which is normally a strict subset
///   of the linked window.
///   </description></item>
/// </list>
/// <para>
/// Slot accounting uses a single mutex to keep <c>_totalRequestSlots</c> and
/// <c>_leasedSlots</c> mutually consistent for <see cref="AcquireSlots"/>
/// (compare-then-set). <see cref="GetState"/> reads under the same mutex so
/// snapshots are internally consistent.
/// </para>
/// </remarks>
internal sealed class RuntimeStateManager : IRuntimeStateManager
{
    // [CS-TODO] Make configurable. Hard-coded to match the current App Server contract.
    private const int MaxLinkedWorkersValue = 20;

    private readonly ConcurrentDictionary<string, byte> _linkedWorkers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _workerCapacities = new(StringComparer.Ordinal);
    private readonly object _slotLock = new();
    private readonly ILogger<RuntimeStateManager> _logger;
    private TaskCompletionSource<object> _slotAvailabilitySignal = CreateSlotAvailabilitySignal();

    private int _totalRequestSlots;
    private int _leasedSlots;
    private int _stoppingFlag;

    public RuntimeStateManager(ILogger<RuntimeStateManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public event Action StateChanged;

    private bool IsStopping => Volatile.Read(ref _stoppingFlag) != 0;

    /// <inheritdoc/>
    public RuntimeState GetState()
    {
        // While stopping, the runtime refuses to advertise any slots: total and
        // available are both clamped to zero so the App Server stops routing
        // new work here immediately, without waiting for every worker's
        // OnWorkerCapacityUnavailable to be called during parallel drain.
        // LinkedWorkerCount is intentionally not clamped — operators still
        // want visibility into how many workers are draining.
        if (IsStopping)
        {
            return new RuntimeState
            {
                MaxLinkedWorkers = MaxLinkedWorkersValue,
                LinkedWorkerCount = _linkedWorkers.Count,
                TotalRequestSlots = 0,
                TotalAvailableRequestSlots = 0
            };
        }

        int total;
        int leased;

        lock (_slotLock)
        {
            total = _totalRequestSlots;
            leased = _leasedSlots;
        }

        return new RuntimeState
        {
            MaxLinkedWorkers = MaxLinkedWorkersValue,
            LinkedWorkerCount = _linkedWorkers.Count,
            TotalRequestSlots = total,
            TotalAvailableRequestSlots = Math.Max(0, total - leased)
        };
    }

    /// <inheritdoc/>
    public void OnWorkerLinked(string workerId)
    {
        if (string.IsNullOrEmpty(workerId))
        {
            throw new ArgumentException("Worker id is required.", nameof(workerId));
        }

        if (!_linkedWorkers.TryAdd(workerId, 0))
        {
            _logger.LogDebug("Worker '{workerId}' is already linked; ignoring duplicate.", workerId);
            return;
        }

        _logger.LogInformation("Worker '{workerId}' linked. Linked worker count: {count}.", workerId, _linkedWorkers.Count);
        RaiseStateChanged();
    }

    /// <inheritdoc/>
    public void OnWorkerUnlinked(string workerId)
    {
        if (string.IsNullOrEmpty(workerId))
        {
            throw new ArgumentException("Worker id is required.", nameof(workerId));
        }

        if (!_linkedWorkers.TryRemove(workerId, out _))
        {
            return;
        }

        _logger.LogInformation("Worker '{workerId}' unlinked. Linked worker count: {count}.", workerId, _linkedWorkers.Count);
        RaiseStateChanged();
    }

    /// <inheritdoc/>
    public void OnWorkerCapacityAvailable(string workerId, int slotCapacity)
    {
        if (string.IsNullOrEmpty(workerId))
        {
            throw new ArgumentException("Worker id is required.", nameof(workerId));
        }

        if (slotCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCapacity), "Slot capacity must be positive.");
        }

        if (!_workerCapacities.TryAdd(workerId, slotCapacity))
        {
            _logger.LogDebug("Worker '{workerId}' already has capacity tracked; ignoring duplicate.", workerId);
            return;
        }

        lock (_slotLock)
        {
            _totalRequestSlots += slotCapacity;
        }

        SignalSlotAvailabilityChanged();

        _logger.LogInformation(
            "Worker '{workerId}' contributed slot capacity {slotCapacity}. Total slots: {totalSlots}.",
            workerId,
            slotCapacity,
            _totalRequestSlots);

        RaiseStateChanged();
    }

    /// <inheritdoc/>
    public void OnWorkerCapacityUnavailable(string workerId)
    {
        if (string.IsNullOrEmpty(workerId))
        {
            throw new ArgumentException("Worker id is required.", nameof(workerId));
        }

        if (!_workerCapacities.TryRemove(workerId, out int capacity))
        {
            return;
        }

        lock (_slotLock)
        {
            _totalRequestSlots -= capacity;
        }

        _logger.LogInformation(
            "Worker '{workerId}' capacity withdrawn; reclaimed {slotCapacity} slot(s). Total slots: {totalSlots}.",
            workerId,
            capacity,
            _totalRequestSlots);

        RaiseStateChanged();
    }

    /// <inheritdoc/>
    public int AcquireSlots(int requestedSlotCount)
    {
        if (requestedSlotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSlotCount), "Requested slot count must be positive.");
        }

        // No new leases once the runtime is stopping. Matches the GetState()
        // promise that available slots are zero from the moment SetStopping
        // is called.
        if (IsStopping)
        {
            return 0;
        }

        int granted;
        lock (_slotLock)
        {
            int available = Math.Max(0, _totalRequestSlots - _leasedSlots);
            granted = Math.Min(requestedSlotCount, available);
            _leasedSlots += granted;
        }

        if (granted > 0)
        {
            RaiseStateChanged();
        }

        return granted;
    }

    /// <inheritdoc/>
    public async Task<int> AcquireSlotsAsync(
        int requestedSlotCount,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (requestedSlotCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSlotCount), "Requested slot count must be positive.");
        }

        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must not be negative.");
        }

        if (timeout == TimeSpan.Zero)
        {
            return TryAcquireSlots(requestedSlotCount);
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        while (true)
        {
            Task waitTask;
            int granted;

            lock (_slotLock)
            {
                if (IsStopping)
                {
                    return 0;
                }

                int available = Math.Max(0, _totalRequestSlots - _leasedSlots);
                if (available > 0)
                {
                    granted = Math.Min(requestedSlotCount, available);
                    _leasedSlots += granted;
                    waitTask = Task.CompletedTask;
                }
                else
                {
                    granted = 0;
                    waitTask = _slotAvailabilitySignal.Task;
                }
            }

            if (granted > 0)
            {
                RaiseStateChanged();
                return granted;
            }

            try
            {
                await waitTask.WaitAsync(linkedSource.Token);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
        }
    }

    /// <inheritdoc/>
    public void ReleaseSlots(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Slot count must be positive.");
        }

        int over;
        int released;

        lock (_slotLock)
        {
            over = Math.Max(0, count - _leasedSlots);
            _leasedSlots = Math.Max(0, _leasedSlots - count);
        }

        released = count - over;

        if (over > 0)
        {
            _logger.LogWarning(
                "Attempted to release {count} slot(s) but only {available} were leased; clamped at zero.",
                count,
                released);
        }

        // Mirror AcquireSlots: only notify when state actually changed. A fully
        // over-released call (e.g. release after the lease count has already
        // been reset to zero by SetStopping) is a no-op and should not trigger
        // a debounced publish to the mesh service.
        if (released > 0)
        {
            SignalSlotAvailabilityChanged();
            RaiseStateChanged();
        }
    }

    /// <inheritdoc/>
    public void SetStopping()
    {
        // One-way latch. Interlocked ensures the StateChanged event is raised
        // exactly once for this transition, even under concurrent callers.
        if (Interlocked.Exchange(ref _stoppingFlag, 1) != 0)
        {
            return;
        }

        _logger.LogInformation("Runtime marked as stopping; request slots are no longer advertised.");
        SignalSlotAvailabilityChanged();
        RaiseStateChanged();
    }

    private int TryAcquireSlots(int requestedSlotCount)
    {
        if (IsStopping)
        {
            return 0;
        }

        int granted;
        lock (_slotLock)
        {
            int available = Math.Max(0, _totalRequestSlots - _leasedSlots);
            granted = Math.Min(requestedSlotCount, available);
            _leasedSlots += granted;
        }

        if (granted > 0)
        {
            RaiseStateChanged();
        }

        return granted;
    }

    private static TaskCompletionSource<object> CreateSlotAvailabilitySignal()
    {
        return new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void SignalSlotAvailabilityChanged()
    {
        TaskCompletionSource<object> signal;
        lock (_slotLock)
        {
            signal = _slotAvailabilitySignal;
            _slotAvailabilitySignal = CreateSlotAvailabilitySignal();
        }

        signal.TrySetResult(null);
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // A faulty subscriber must not break the mutating path.
            _logger.LogError(ex, "Error invoking {eventName} handler.", nameof(StateChanged));
        }
    }
}
