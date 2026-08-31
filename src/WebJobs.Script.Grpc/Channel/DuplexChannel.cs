// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

/// <summary>
/// Represents an owned bidirectional message channel.
/// </summary>
/// <typeparam name="T">The transported message type.</typeparam>
public abstract class DuplexChannel<T> : Channel<T>, IAsyncDisposable
{
    private readonly Lock _disposeLock = new();
    private Task _disposeTask;

    /// <summary>
    /// Asynchronously releases the channel and its owned resources.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        if (_disposeTask is { } task)
        {
            return new ValueTask(task);
        }

        lock (_disposeLock)
        {
            _disposeTask ??= DisposeAsyncCore().AsTask();
            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>
    /// Asynchronously releases resources owned by the derived channel.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    protected abstract ValueTask DisposeAsyncCore();
}
