// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Writes worker system-log records directly to standard output.
/// </summary>
internal sealed class WorkerSystemLogWriter : IWorkerSystemLogWriter, IDisposable
{
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(initialCount: 1, maxCount: 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerSystemLogWriter"/> class.
    /// </summary>
    public WorkerSystemLogWriter()
        : this(Console.Out)
    {
    }

    internal WorkerSystemLogWriter(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(string record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(record);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(record.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            throw new WorkerSystemLogEmissionException("Writing the worker system log to stdout failed.", exception);
        }
        catch (ObjectDisposedException exception)
        {
            throw new WorkerSystemLogEmissionException("Worker Proxy stdout is unavailable.", exception);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
