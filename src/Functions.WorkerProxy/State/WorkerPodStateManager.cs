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
/// Both startup modes are retained as identity; mode-specific preparation is not implemented.
/// All state decisions and snapshot replacements share one lock, including assignment racing with termination.
/// Readers can retain a snapshot after releasing the lock because subsequent transitions replace rather than mutate it.
/// </remarks>
internal sealed class WorkerPodStateManager
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);
    private readonly Lock _stateLock = new();
    private readonly HashSet<TaskCompletionSource<WorkerPodState>> _waiters = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeProvider _neverLinkedClock;
    private readonly TimeSpan _neverLinkedTimeout;
    private readonly long _startupTimestamp;
    private readonly TaskCompletionSource _runtimeLinkAccepted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WorkerPodState _state;
    private WorkerAssignment? _assignment;
    private NeverLinkedOutcome _neverLinkedOutcome;
    private bool _applicationStopping;

    // High-water mark for accepted or terminated sessions, including sessions that never had a worker.
    private long _lastSessionId;
    private int _assignmentClaimed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerPodStateManager"/> class.
    /// Creates the pod's initial state and captures its startup time.
    /// </summary>
    /// <param name="options">The worker proxy configuration containing the pod identity.</param>
    /// <param name="timeProvider">The clock used for startup time and state-poll deadlines.</param>
    public WorkerPodStateManager(
        IOptions<WorkerProxyOptions> options,
        TimeProvider timeProvider)
        : this(options, timeProvider, WorkerProxyStartupTime.Capture(timeProvider))
    {
    }

    public WorkerPodStateManager(
        IOptions<WorkerProxyOptions> options,
        TimeProvider timeProvider,
        WorkerProxyStartupTime startupTime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(startupTime);

        string podName = options.Value.PodName;
        ArgumentException.ThrowIfNullOrWhiteSpace(podName);

        _timeProvider = timeProvider;
        _neverLinkedClock = startupTime.Clock;
        _neverLinkedTimeout = options.Value.NeverLinkedTimeout;
        _startupTimestamp = startupTime.Timestamp;
        _state = WorkerPodState.CreateUnassigned(podName, startupTime.UtcTime);
    }

    private enum NeverLinkedOutcome
    {
        Pending,
        RuntimeLinkAccepted,
        NeverLinkedRemovalRequested,
        TerminalRemovalRequested
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
    /// Captures the pod state and effective HTTP origin for one worker session under the assignment lock.
    /// </summary>
    public (WorkerPodState State, string? HttpProxyEndpoint) GetWorkerCapabilitySnapshot(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        lock (_stateLock)
        {
            string? httpProxyEndpoint = _state.SessionId == sessionId
                && _state.AssignmentState is WorkerAssignmentState.Unassigned or WorkerAssignmentState.Ready
                    ? _assignment?.ResolvedHttpProxyEndpoint?.AbsoluteUri
                    : null;

            return (_state, httpProxyEndpoint);
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
            if (sessionId <= _lastSessionId ||
                _state.AssignmentState == WorkerAssignmentState.Failed ||
                _state.RemovalRequested)
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
            if (_state.SessionId != sessionId || !_state.IsWorkerAttached || _state.RemovalRequested)
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

            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                WorkerId = workerId,
                AssignmentState = _assignment is null
                    ? WorkerAssignmentState.Unassigned
                    : WorkerAssignmentState.Ready
            });
            return true;
        }
    }

    /// <summary>
    /// Records identity supplied by an admitted Runtime stream, not by worker messages.
    /// </summary>
    /// <param name="sessionId">The attaching relay session identifier.</param>
    /// <param name="identity">The optional Runtime identity supplied in stream metadata.</param>
    /// <returns>Whether the Runtime attachment is accepted for the current pod state.</returns>
    public bool OnRuntimeAttached(long sessionId, PodIdentity? identity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        lock (_stateLock)
        {
            if (sessionId < _lastSessionId ||
                _state.AssignmentState == WorkerAssignmentState.Failed ||
                _state.RemovalRequested)
            {
                return false;
            }

            if (identity is not null &&
                _state.FunctionAppName is { } app &&
                !string.Equals(app, identity.App.FunctionAppName, StringComparison.Ordinal))
            {
                return false;
            }

            if (_neverLinkedOutcome == NeverLinkedOutcome.Pending &&
                _neverLinkedClock.GetElapsedTime(_startupTimestamp) >= _neverLinkedTimeout)
            {
                SelectRemovalRequestedLocked();
            }

            if (_neverLinkedOutcome == NeverLinkedOutcome.Pending)
            {
                _neverLinkedOutcome = NeverLinkedOutcome.RuntimeLinkAccepted;
                _runtimeLinkAccepted.TrySetResult();
            }

            if (_neverLinkedOutcome != NeverLinkedOutcome.RuntimeLinkAccepted)
            {
                return false;
            }

            if (identity is null)
            {
                return true;
            }

            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                RuntimeIdentity = identity
            });

            return true;
        }
    }

    /// <summary>
    /// Publishes a correlated successful WorkerInit handshake for the current identified Runtime.
    /// </summary>
    /// <param name="sessionId">The relay session that wrote the response to the Runtime.</param>
    /// <returns>Whether Runtime initialization was published.</returns>
    public bool OnRuntimeInitialized(long sessionId)
    {
        lock (_stateLock)
        {
            if (_state.SessionId != sessionId ||
                _assignment is null || !_state.IsWorkerReady ||
                _state.RuntimeIdentity is null || _state.RuntimeInitialized)
            {
                return false;
            }

            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                RuntimeInitialized = true
            });
            return true;
        }
    }

    /// <summary>
    /// Withdraws readiness on relay termination or worker detachment. Old notifications have no effect.
    /// </summary>
    public bool OnSessionTerminated(long sessionId) =>
        OnSessionTerminated(sessionId, workerStreamFailed: false);

    internal bool OnSessionTerminated(long sessionId, bool workerStreamFailed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        lock (_stateLock)
        {
            if (_state.RemovalRequested)
            {
                return false;
            }

            if (sessionId < _lastSessionId)
            {
                return false;
            }

            if (_state.IsWorkerAttached && _state.SessionId != sessionId)
            {
                throw new InvalidOperationException("A different worker session is still attached.");
            }

            bool establishedWorkerSession = _state.IsWorkerAttached &&
                _state.SessionId == sessionId &&
                _state.WorkerId is not null;

            // A runtime-only session can terminate before any worker attaches. Fence off its late notifications.
            _lastSessionId = sessionId;
            if (!_state.IsWorkerAttached)
            {
                if (_state.RuntimeIdentity is not null)
                {
                    UpdateStateAndNotifyWaitersLocked(_state with
                    {
                        Revision = checked(_state.Revision + 1),
                        RuntimeIdentity = null,
                        RuntimeInitialized = false
                    });
                    return true;
                }

                return false;
            }

            bool workerRemovalRequested = workerStreamFailed &&
                establishedWorkerSession &&
                !_applicationStopping;
            if (workerRemovalRequested &&
                _neverLinkedOutcome == NeverLinkedOutcome.Pending)
            {
                _neverLinkedOutcome = NeverLinkedOutcome.TerminalRemovalRequested;
                _runtimeLinkAccepted.TrySetResult();
            }

            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                IsWorkerAttached = false,
                RuntimeIdentity = null,
                RuntimeInitialized = false,
                RemovalRequested = _state.RemovalRequested || workerRemovalRequested,
                // Any assigned worker-session loss requires the pod owner to replace this unhealthy pod.
                AssignmentState = _assignment is null
                    ? WorkerAssignmentState.Unassigned
                    : WorkerAssignmentState.Failed
            });
            return true;
        }
    }

    /// <summary>
    /// Publishes sticky removal and clears live worker and assignment identity.
    /// </summary>
    public bool RequestRemoval()
    {
        lock (_stateLock)
        {
            if (_state.RemovalRequested)
            {
                return false;
            }

            if (_neverLinkedOutcome == NeverLinkedOutcome.Pending)
            {
                _neverLinkedOutcome = NeverLinkedOutcome.TerminalRemovalRequested;
                _runtimeLinkAccepted.TrySetResult();
            }

            _assignment = null;
            _assignmentClaimed = 0;
            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                SessionId = null,
                IsWorkerAttached = false,
                WorkerId = null,
                AssignmentState = WorkerAssignmentState.Unassigned,
                StartupMode = null,
                FunctionAppName = null,
                FunctionGroupName = null,
                MaxHttpConcurrency = null,
                IsAlwaysReady = null,
                RuntimeIdentity = null,
                RuntimeInitialized = false,
                RemovalRequested = true
            });
            return true;
        }
    }

    /// <summary>
    /// Records or replays assignment atomically with worker lifecycle and terminal state.
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

            if (_state.RemovalRequested &&
                _neverLinkedOutcome != NeverLinkedOutcome.NeverLinkedRemovalRequested)
            {
                return WorkerAssignmentResult.WorkerNotReady;
            }

            if (_state.RuntimeIdentity is { } runtime &&
                !string.Equals(runtime.App.FunctionAppName, assignment.FunctionAppName, StringComparison.Ordinal))
            {
                return WorkerAssignmentResult.AssignmentConflict;
            }

            // Assignment must precede StartStream so startup dependencies can use it.
            WorkerPodState assigned = _state with
            {
                Revision = checked(_state.Revision + 1),
                AssignmentState = _state.IsWorkerReady
                    ? WorkerAssignmentState.Ready
                    : WorkerAssignmentState.Assigned,
                StartupMode = assignment.StartupMode,
                FunctionAppName = assignment.FunctionAppName,
                FunctionGroupName = assignment.FunctionGroupName,
                MaxHttpConcurrency = _state.RemovalRequested ? 0 : assignment.MaxHttpConcurrency,
                IsAlwaysReady = assignment.IsAlwaysReady
            };

            // The gate and publication share the lifecycle lock so only one identity is reserved.
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
    /// Prevents intentional application shutdown from being classified as worker loss.
    /// </summary>
    public void OnApplicationStopping()
    {
        lock (_stateLock)
        {
            _applicationStopping = true;
        }
    }

    /// <summary>
    /// Waits for the fixed startup deadline or an accepted Runtime connection.
    /// </summary>
    public async Task WatchNeverLinkedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining;
            lock (_stateLock)
            {
                if (_neverLinkedOutcome != NeverLinkedOutcome.Pending)
                {
                    return;
                }

                remaining = _neverLinkedTimeout - _neverLinkedClock.GetElapsedTime(_startupTimestamp);
                if (remaining <= TimeSpan.Zero)
                {
                    SelectRemovalRequestedLocked();
                    return;
                }
            }

            try
            {
                await _runtimeLinkAccepted.Task.WaitAsync(remaining, _neverLinkedClock, cancellationToken);
                return;
            }
            catch (TimeoutException)
            {
                // Recheck the monotonic deadline in case the timer completed early.
            }
        }
    }

    private void SelectRemovalRequestedLocked()
    {
        if (_neverLinkedOutcome == NeverLinkedOutcome.Pending)
        {
            _neverLinkedOutcome = NeverLinkedOutcome.NeverLinkedRemovalRequested;
            UpdateStateAndNotifyWaitersLocked(_state with
            {
                Revision = checked(_state.Revision + 1),
                RemovalRequested = true,
                MaxHttpConcurrency = 0
            });
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
