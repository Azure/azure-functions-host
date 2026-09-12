// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Owns single-worker assignment and readiness for the lifetime of a WorkerProxy pod.
/// </summary>
/// <remarks>
/// Lifecycle notifications use the relay's monotonically increasing session IDs.
/// Assignment is synchronous bookkeeping; it never initializes or specializes a worker.
/// All state decisions and snapshot replacements share one lock, including assignment racing with termination.
/// Readers can retain a snapshot after releasing the lock because subsequent transitions replace rather than mutate it.
/// </remarks>
internal sealed class WorkerPodStateManager
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);
    private readonly Lock _stateLock = new();
    private readonly HashSet<TaskCompletionSource<WorkerPodState>> _waiters = [];
    private readonly TimeProvider _timeProvider;
    private WorkerPodState _state;
    private WorkerAssignment? _assignment;
    // High-water mark for accepted or terminated sessions, including sessions that never had a worker.
    private long _lastSessionId;
    private int _assignmentClaimed;

    public WorkerPodStateManager(
        IOptions<WorkerProxyOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        string podName = options.Value.PodName;
        ArgumentException.ThrowIfNullOrWhiteSpace(podName);

        _timeProvider = timeProvider;
        _state = WorkerPodState.CreateUnassigned(podName);
    }

    /// <summary>
    /// Gets the current immutable state without incrementing its revision.
    /// </summary>
    public WorkerPodState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    internal int PendingWaiterCount
    {
        get
        {
            lock (_stateLock)
            {
                return _waiters.Count;
            }
        }
    }

    /// <summary>
    /// Returns a newer snapshot immediately, or waits up to 60 seconds for a state change.
    /// </summary>
    /// <returns>A changed snapshot, or <see cref="WorkerStatePollResult.NoChange"/> on timeout.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The last known revision is negative or greater than the current revision.
    /// </exception>
    /// <exception cref="OperationCanceledException">The caller canceled the poll.</exception>
    public async Task<WorkerStatePollResult> WaitForChangeAsync(
        long lastKnownRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastKnownRevision);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<WorkerPodState> waiter;
        lock (_stateLock)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(lastKnownRevision, _state.Revision);
            if (lastKnownRevision < _state.Revision)
            {
                return new WorkerStatePollResult(_state);
            }

            // Compare and register under the publication lock so a transition cannot slip between them.
            waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(waiter);
        }

        try
        {
            WorkerPodState state = await waiter.Task.WaitAsync(PollTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);
            return new WorkerStatePollResult(state);
        }
        catch (TimeoutException)
        {
            // A transition may have won the publication lock just as the timer expired.
            lock (_stateLock)
            {
                return _state.Revision > lastKnownRevision
                    ? new WorkerStatePollResult(_state)
                    : WorkerStatePollResult.NoChange;
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    /// <summary>
    /// Records an accepted worker attachment. Returns false for old sessions or a terminal assignment.
    /// </summary>
    public bool OnWorkerAttached(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        lock (_stateLock)
        {
            // Pre-assignment transport sessions may be replaced; an assigned pod's failure is terminal.
            if (sessionId <= _lastSessionId || _state.AssignmentState == WorkerAssignmentState.Failed)
            {
                return false;
            }

            if (_state.IsWorkerAttached)
            {
                throw new InvalidOperationException("The current worker session must terminate before another worker attaches.");
            }

            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                SessionId = sessionId,
                IsWorkerAttached = true,
                WorkerId = null
            });
            _lastSessionId = sessionId;
            return true;
        }
    }

    /// <summary>
    /// Records the validated first StartStream. Returns false for stale, detached, or repeated notifications.
    /// </summary>
    /// <remarks>
    /// The relay must validate the first message's type before calling this method.
    /// This manager validates the worker ID but does not parse FunctionRpc messages.
    /// </remarks>
    public bool OnWorkerStartStream(long sessionId, string workerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        lock (_stateLock)
        {
            if (_state.SessionId != sessionId || !_state.IsWorkerAttached)
            {
                return false;
            }

            if (_state.WorkerId is not null)
            {
                if (!string.Equals(_state.WorkerId, workerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A worker session cannot change its StartStream identity.");
                }

                return false;
            }

            UpdateStateAndNotifyWaitersLocked(_state with { Revision = checked(_state.Revision + 1), WorkerId = workerId });
            return true;
        }
    }

    /// <summary>
    /// Withdraws readiness on relay termination or worker detachment. Old notifications have no effect.
    /// </summary>
    public bool OnSessionTerminated(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        lock (_stateLock)
        {
            if (sessionId < _lastSessionId)
            {
                return false;
            }

            if (_state.IsWorkerAttached && _state.SessionId != sessionId)
            {
                throw new InvalidOperationException("A different worker session is still attached.");
            }

            // A runtime-only session can terminate before any worker attaches. Fence off its late notifications.
            _lastSessionId = sessionId;
            if (!_state.IsWorkerAttached)
            {
                return false;
            }

            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                IsWorkerAttached = false,
                // Preserve assignment identity so equivalent retries report failure and different assignments still conflict.
                AssignmentState = _assignment is null ? WorkerAssignmentState.Unassigned : WorkerAssignmentState.Failed
            });
            return true;
        }
    }

    /// <summary>
    /// Records or replays assignment atomically with worker readiness and terminal state.
    /// </summary>
    public WorkerAssignmentResult Assign(WorkerAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        lock (_stateLock)
        {
            // Replay/conflict takes precedence over current readiness, including after the assigned stream terminates.
            if (_assignment is not null)
            {
                if (!_assignment.IsEquivalentTo(assignment))
                {
                    return WorkerAssignmentResult.AssignmentConflict;
                }

                return _state.AssignmentState == WorkerAssignmentState.Failed
                    ? WorkerAssignmentResult.WorkerTerminated
                    : WorkerAssignmentResult.AlreadyAssigned;
            }

            if (!_state.IsWorkerReady)
            {
                return WorkerAssignmentResult.WorkerNotReady;
            }

            // No asynchronous specialization occurs: publish Ready directly, with a single revision increment.
            WorkerPodState assigned = _state with
            {
                Revision = checked(_state.Revision + 1),
                AssignmentState = WorkerAssignmentState.Ready,
                FunctionAppName = assignment.FunctionAppName,
                FunctionGroupName = assignment.FunctionGroupName,
                IsAlwaysReady = assignment.IsAlwaysReady
            };

            // The gate and publication share the lifecycle lock, so a not-ready call never reserves identity.
            if (Interlocked.CompareExchange(ref _assignmentClaimed, 1, 0) != 0)
            {
                throw new InvalidOperationException("Assignment identity was claimed without a recorded assignment.");
            }

            _assignment = assignment;
            UpdateStateAndNotifyWaitersLocked(assigned);
            return WorkerAssignmentResult.Created;
        }
    }

    /// <summary>
    /// Stores the new state and completes all pending polls with that snapshot, then clears the waiters.
    /// </summary>
    /// <remarks>
    /// The caller must hold <see cref="_stateLock"/>. Waiting callers resume asynchronously,
    /// rather than running inline when their tasks are completed.
    /// </remarks>
    private void UpdateStateAndNotifyWaitersLocked(WorkerPodState state)
    {
        _state = state;
        foreach (TaskCompletionSource<WorkerPodState> waiter in _waiters)
        {
            waiter.TrySetResult(state);
        }

        _waiters.Clear();
    }
}
