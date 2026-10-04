// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Simulates the ScriptHost state and its change wait.
/// </summary>
/// <param name="initialState">The initial ScriptHost state.</param>
internal sealed class ScriptHostStateSignal(ScriptHostState initialState)
{
    private readonly Lock _lock = new();
    private ScriptHostState _state = initialState;
    private long _version;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the current ScriptHost state.
    /// </summary>
    public ScriptHostState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the current lifecycle version.
    /// </summary>
    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Sets the ScriptHost state and wakes the pending waits.
    /// </summary>
    /// <param name="state">The new ScriptHost state.</param>
    public void Set(ScriptHostState state)
    {
        lock (_lock)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            SignalHostChange();
        }
    }

    /// <summary>
    /// Records an active-host change and wakes pending waiters.
    /// </summary>
    public void SignalHostChange()
    {
        lock (_lock)
        {
            _version++;
            TaskCompletionSource changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult();
        }
    }

    /// <summary>
    /// Waits until the version exceeds <paramref name="lastKnownVersion"/>.
    /// </summary>
    /// <param name="lastKnownVersion">The last observed version.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A version greater than <paramref name="lastKnownVersion"/>.</returns>
    public async Task<long> WaitForChangeAsync(long lastKnownVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            Task changed;
            lock (_lock)
            {
                if (_version > lastKnownVersion)
                {
                    return _version;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }
}
