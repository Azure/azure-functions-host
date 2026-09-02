// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.WorkerProxy.ExtensionRpc;

/// <summary>
/// Coordinates byte-based flow-control credits for one direction of an extension RPC call.
/// </summary>
/// <param name="initialCredits">The number of bytes initially available to the sender.</param>
internal sealed class ExtensionRpcCreditWindow(ulong initialCredits)
{
    private readonly Lock _syncLock = new();
    private ulong _available = initialCredits;
    private TaskCompletionSource _changed = CreateChangedSource();

    /// <summary>
    /// Adds byte credits granted by the receiver.
    /// </summary>
    /// <param name="credits">The number of credits to add.</param>
    public void Add(ulong credits)
    {
        if (credits is 0)
        {
            return;
        }

        TaskCompletionSource changed;
        lock (_syncLock)
        {
            _available = ulong.MaxValue - _available < credits ? ulong.MaxValue : _available + credits;
            changed = _changed;
            _changed = CreateChangedSource();
        }

        changed.TrySetResult();
    }

    /// <summary>
    /// Waits for and reserves the requested number of byte credits.
    /// </summary>
    /// <param name="credits">The number of credits to reserve.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when the credits have been reserved.</returns>
    public async ValueTask ReserveAsync(ulong credits, CancellationToken cancellationToken)
    {
        if (credits is 0)
        {
            return;
        }

        while (true)
        {
            Task waitTask;
            lock (_syncLock)
            {
                if (_available >= credits)
                {
                    _available -= credits;
                    return;
                }

                waitTask = _changed.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource CreateChangedSource()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
