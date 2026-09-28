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
    private volatile ScriptHostState _state = initialState;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the current ScriptHost state.
    /// </summary>
    public ScriptHostState State => _state;

    /// <summary>
    /// Sets the ScriptHost state and wakes the pending waits.
    /// </summary>
    /// <param name="state">The new ScriptHost state.</param>
    public void Set(ScriptHostState state)
    {
        _state = state;
        Interlocked.Exchange(ref _changed, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    /// <summary>
    /// Waits until the state differs from <paramref name="lastKnownState"/>.
    /// </summary>
    /// <param name="lastKnownState">The state the caller last observed.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The state that differs from <paramref name="lastKnownState"/>.</returns>
    public async Task<ScriptHostState> WaitForChangeAsync(ScriptHostState lastKnownState, CancellationToken cancellationToken)
    {
        while (true)
        {
            // Capture the signal before reading the state so a change between the two reads is not missed.
            Task changed = Volatile.Read(ref _changed).Task;
            ScriptHostState state = State;
            if (state != lastKnownState)
            {
                return state;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }
}
