// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Manages the internal state machine for the worker pod and provides
/// long-polling notifications for <c>/instanceState</c>.
/// Thread-safe. Follows the same pattern as the Go Proxy's <c>FunctionsRecord</c>.
/// </summary>
internal sealed class WorkerPodStateManager
{
    private static readonly TimeSpan LongPollTimeout = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly List<TaskCompletionSource<WorkerPodState>> _listeners = [];

    private WorkerPodStatus _currentStatus = WorkerPodStatus.None;
    private WorkerPodHealthStatus _currentHealthStatus = WorkerPodHealthStatus.None;
    private int _revisionId;

    /// <summary>
    /// Gets the current pod status.
    /// </summary>
    public WorkerPodStatus CurrentStatus
    {
        get { lock (_lock) { return _currentStatus; } }
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
    /// Updates the health status and notifies all long-polling listeners.
    /// </summary>
    public void UpdateHealthStatus(WorkerPodHealthStatus newStatus)
    {
        lock (_lock)
        {
            if (_currentHealthStatus == newStatus)
            {
                return;
            }

            _currentHealthStatus = newStatus;
            _revisionId++;

            NotifyListeners();
        }
    }

    /// <summary>
    /// Returns the current state if it has changed since <paramref name="clientRevisionId"/>,
    /// otherwise blocks until a state change occurs or the long-poll timeout expires.
    /// </summary>
    /// <param name="clientRevisionId">The client's last known revision ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state if changed, or <see langword="null"/> on timeout (204 No Content).</returns>
    public async Task<WorkerPodState?> WaitForChangeAsync(int clientRevisionId, CancellationToken cancellationToken)
    {
        TaskCompletionSource<WorkerPodState>? listener = null;

        lock (_lock)
        {
            if (_revisionId != clientRevisionId)
            {
                return BuildState();
            }

            listener = new TaskCompletionSource<WorkerPodState>(TaskCreationOptions.RunContinuationsAsynchronously);
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
    public WorkerPodState GetCurrentState()
    {
        lock (_lock)
        {
            return BuildState();
        }
    }

    private WorkerPodState BuildState()
    {
        return new WorkerPodState
        {
            CurrentPodStatusTransition = new PodStatusTransition
            {
                ToPodStatus = _currentStatus
            },
            CurrentPodHealthStatusTransition = new PodHealthStatusTransition
            {
                ToPodStatus = _currentHealthStatus
            },
            ChangeFlags = WorkerPodChangeFlags.PodStatus | WorkerPodChangeFlags.HealthStatus,
            RevisionId = _revisionId
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
}
