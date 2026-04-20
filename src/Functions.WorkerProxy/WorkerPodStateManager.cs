// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Manages the internal state machine for the worker pod and provides
/// long-polling notifications for <c>POST /admin/infra/instanceState</c>.
/// Thread-safe. Publishes the <c>FunctionsWorkerPod</c> schema defined in the Goal 3 design doc.
/// </summary>
internal sealed class WorkerPodStateManager
{
    private static readonly TimeSpan LongPollTimeout = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly List<TaskCompletionSource<WorkerInstanceState>> _listeners = [];
    private readonly string _podName;

    private WorkerPodStatus _currentStatus = WorkerPodStatus.None;
    private int _revisionId;

    // Identity fields set during assign.
    private string? _functionGroupName;
    private bool _isAlwaysReady;

    // Correlation — set after link (future).
    private string? _runtimePodName;

    // Drain state — first-reason-wins, sticky replacementPolicy.
    private DrainReason? _drainReason;
    private ReplacementPolicy? _replacementPolicy;

    public WorkerPodStateManager(RelayOptions options)
    {
        _podName = options?.PodName ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the current pod status.
    /// </summary>
    public WorkerPodStatus CurrentStatus
    {
        get { lock (_lock) { return _currentStatus; } }
    }

    /// <summary>
    /// Gets whether a drain has already been accepted.
    /// </summary>
    public bool IsDraining
    {
        get { lock (_lock) { return _drainReason is not null; } }
    }

    /// <summary>
    /// Updates the pod status and notifies all long-polling listeners.
    /// </summary>
    public void UpdatePodStatus(WorkerPodStatus newStatus)
    {
        lock (_lock)
        {
            if (_currentStatus == newStatus)
            {
                return;
            }

            _currentStatus = newStatus;
            _revisionId++;

            NotifyListeners();
        }
    }

    /// <summary>
    /// Stores identity fields from the assign request so they appear in <c>instanceState</c> responses.
    /// </summary>
    public void SetAssignMetadata(string? functionGroupName, bool isAlwaysReady)
    {
        lock (_lock)
        {
            _functionGroupName = functionGroupName;
            _isAlwaysReady = isAlwaysReady;
            _revisionId++;

            NotifyListeners();
        }
    }

    /// <summary>
    /// Sets the linked runtime pod name for correlation in <c>instanceState</c> responses.
    /// </summary>
    public void SetRuntimePodName(string runtimePodName)
    {
        lock (_lock)
        {
            _runtimePodName = runtimePodName;
            _revisionId++;

            NotifyListeners();
        }
    }

    /// <summary>
    /// Accepts a drain request with the given reason. First-reason-wins: subsequent calls
    /// are idempotent and do not change the persisted reason or derived replacement policy.
    /// Transitions <c>podStatus</c> to <see cref="WorkerPodStatus.Draining"/>.
    /// </summary>
    /// <returns><see langword="true"/> if this was the first accepted drain; <see langword="false"/> if already draining.</returns>
    public bool AcceptDrain(DrainReason reason)
    {
        lock (_lock)
        {
            if (_drainReason is not null)
            {
                // Already draining — idempotent success, first reason wins.
                return false;
            }

            _drainReason = reason;
            _replacementPolicy = MapReasonToPolicy(reason, _functionGroupName);
            _currentStatus = WorkerPodStatus.Draining;
            _revisionId++;

            NotifyListeners();
            return true;
        }
    }

    /// <summary>
    /// Returns the current state if it has changed since <paramref name="clientRevision"/>,
    /// otherwise blocks until a state change occurs or the long-poll timeout expires.
    /// </summary>
    /// <param name="clientRevision">The client's last known revision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state if changed, or <see langword="null"/> on timeout (204 No Content).</returns>
    public async Task<WorkerInstanceState?> WaitForChangeAsync(int clientRevision, CancellationToken cancellationToken)
    {
        TaskCompletionSource<WorkerInstanceState>? listener = null;

        lock (_lock)
        {
            if (_revisionId != clientRevision)
            {
                return BuildState();
            }

            listener = new TaskCompletionSource<WorkerInstanceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            _listeners.Add(listener);
        }

        try
        {
            var result = await listener.Task.WaitAsync(LongPollTimeout, cancellationToken);
            return result;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (_lock)
            {
                _listeners.Remove(listener);
            }
        }
    }

    /// <summary>
    /// Returns the current state snapshot.
    /// </summary>
    public WorkerInstanceState GetCurrentState()
    {
        lock (_lock)
        {
            return BuildState();
        }
    }

    private WorkerInstanceState BuildState()
    {
        return new WorkerInstanceState
        {
            FunctionsContainerType = "FunctionsWorkerPod",
            PodName = _podName,
            Revision = _revisionId,
            State = new WorkerInstanceStateDetails
            {
                PodStatus = _currentStatus,
                RuntimePodName = _runtimePodName,
                FunctionGroupName = _functionGroupName,
                IsAlwaysReady = _isAlwaysReady,
                ReplacementPolicy = _replacementPolicy
            }
        };
    }

    private void NotifyListeners()
    {
        var state = BuildState();
        foreach (var listener in _listeners)
        {
            listener.TrySetResult(state);
        }

        _listeners.Clear();
    }

    private static ReplacementPolicy MapReasonToPolicy(DrainReason reason, string? functionGroupName)
    {
        if (reason == DrainReason.ReplaceWorkerKeepRuntime
            && string.Equals(functionGroupName, "http", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacementPolicy.SameRuntimeRefill;
        }

        return ReplacementPolicy.NoReplacement;
    }
}
