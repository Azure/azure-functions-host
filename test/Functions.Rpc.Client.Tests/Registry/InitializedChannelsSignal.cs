// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

internal sealed class InitializedChannelsSignal
{
    private readonly Lock _lock = new();
    private long _version;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    internal void Signal()
    {
        lock (_lock)
        {
            _version++;
            TaskCompletionSource changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult();
        }
    }

    internal async Task<long> WaitForChangeAsync(long lastKnownVersion, CancellationToken cancellationToken)
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
