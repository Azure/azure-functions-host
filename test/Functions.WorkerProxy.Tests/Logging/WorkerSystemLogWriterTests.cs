// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Logging;

public class WorkerSystemLogWriterTests
{
    [Fact]
    public async Task WriteAsync_ConcurrentRecordsNeverInterleave()
    {
        ConcurrencyTrackingWriter output = new();
        using WorkerSystemLogWriter writer = new(output);
        Task[] writes = new Task[32];

        for (int i = 0; i < writes.Length; i++)
        {
            writes[i] = writer.WriteAsync($"record-{i}", CancellationToken.None).AsTask();
        }

        await Task.WhenAll(writes);

        Assert.Equal(1, output.MaximumConcurrency);
        Assert.Equal(writes.Length, output.Lines.Count);
    }

    [Fact]
    public async Task WriteAsync_OutputFailureIsSurfaced()
    {
        using WorkerSystemLogWriter writer = new(new FaultingTextWriter());

        WorkerSystemLogEmissionException exception = await Assert.ThrowsAsync<WorkerSystemLogEmissionException>(
            () => writer.WriteAsync("record", CancellationToken.None).AsTask());

        Assert.IsType<IOException>(exception.InnerException);
    }

    private sealed class ConcurrencyTrackingWriter : TextWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();
        private int _activeWrites;
        private int _maximumConcurrency;

        public override Encoding Encoding => Encoding.UTF8;

        public IReadOnlyCollection<string> Lines => _lines;

        public int MaximumConcurrency => _maximumConcurrency;

        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            int activeWrites = Interlocked.Increment(ref _activeWrites);
            InterlockedExtensions.Max(ref _maximumConcurrency, activeWrites);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
                _lines.Enqueue(buffer.ToString());
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
        }
    }

    private sealed class FaultingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            return Task.FromException(new IOException("Injected failure."));
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            int current = Volatile.Read(ref location);
            while (current < value)
            {
                int observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
