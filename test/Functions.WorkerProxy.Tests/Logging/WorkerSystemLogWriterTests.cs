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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Logging;

public class WorkerSystemLogWriterTests
{
    [Fact]
    public void TryEnqueue_ReturnsQueueFullWithoutWaiting()
    {
        using WorkerSystemLogWriter writer = CreateWriter(new StringWriter(), capacity: 1);

        Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, writer.TryEnqueue("first"));
        Assert.Equal(WorkerSystemLogEnqueueResult.QueueFull, writer.TryEnqueue("second"));
    }

    [Fact]
    public async Task TryEnqueue_DoesNotWaitForBlockedStdout()
    {
        BlockingTextWriter output = new();
        using WorkerSystemLogWriter writer = CreateWriter(output, capacity: 2);
        await writer.StartAsync(CancellationToken.None);

        Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, writer.TryEnqueue("first"));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, writer.TryEnqueue("second"));

        output.Release();
        await writer.StopAsync(CancellationToken.None);
        Assert.Equal(["first", "second"], output.Lines);
    }

    [Fact]
    public async Task BackgroundWriter_ConcurrentRecordsNeverInterleave()
    {
        ConcurrencyTrackingWriter output = new();
        using WorkerSystemLogWriter writer = CreateWriter(output, capacity: 32);
        await writer.StartAsync(CancellationToken.None);

        Parallel.For(0, 32, index =>
            Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, writer.TryEnqueue($"record-{index}")));

        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(1, output.MaximumConcurrency);
        Assert.Equal(32, output.Lines.Count);
    }

    [Fact]
    public async Task TryEnqueue_ReturnsPipelineFaultedAfterOutputFailure()
    {
        FaultingTextWriter output = new();
        using WorkerSystemLogWriter writer = CreateWriter(output, capacity: 2);
        await writer.StartAsync(CancellationToken.None);

        Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, writer.TryEnqueue("first"));
        await output.WriteAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        WorkerSystemLogEnqueueResult result = await WaitForResultAsync(
            writer,
            WorkerSystemLogEnqueueResult.PipelineFaulted);

        Assert.Equal(WorkerSystemLogEnqueueResult.PipelineFaulted, result);
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TryEnqueue_ReturnsPipelineStoppingAfterStop()
    {
        using WorkerSystemLogWriter writer = CreateWriter(new StringWriter(), capacity: 1);
        await writer.StartAsync(CancellationToken.None);

        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(WorkerSystemLogEnqueueResult.PipelineStopping, writer.TryEnqueue("record"));
    }

    [Fact]
    public async Task StopAsync_ReturnsAfterDrainTimeout()
    {
        BlockingTextWriter output = new();
        using WorkerSystemLogWriter writer = CreateWriter(
            output,
            capacity: 1,
            shutdownDrainTimeout: TimeSpan.FromMilliseconds(50));
        await writer.StartAsync(CancellationToken.None);
        Assert.Equal(WorkerSystemLogEnqueueResult.Accepted, writer.TryEnqueue("record"));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WorkerSystemLogEnqueueResult.PipelineStopping, writer.TryEnqueue("following"));
        output.Release();
        await writer.StopAsync(CancellationToken.None);
    }

    private static WorkerSystemLogWriter CreateWriter(
        TextWriter output,
        int capacity,
        TimeSpan? shutdownDrainTimeout = null)
    {
        WorkerProxyOptions options = new()
        {
            SystemLogQueueCapacity = capacity,
            SystemLogShutdownDrainTimeout = shutdownDrainTimeout ?? TimeSpan.FromSeconds(10)
        };

        return new WorkerSystemLogWriter(
            output,
            options,
            NullLogger<WorkerSystemLogWriter>.Instance);
    }

    private static async Task<WorkerSystemLogEnqueueResult> WaitForResultAsync(
        WorkerSystemLogWriter writer,
        WorkerSystemLogEnqueueResult expected)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true)
        {
            WorkerSystemLogEnqueueResult result = writer.TryEnqueue("probe");
            if (result == expected)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private sealed class BlockingTextWriter : TextWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Encoding Encoding => Encoding.UTF8;

        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> Lines => _lines;

        public void Release()
        {
            _release.TrySetResult();
        }

        public override async Task WriteLineAsync(string? value)
        {
            WriteStarted.TrySetResult();
            await _release.Task;
            _lines.Enqueue(value ?? string.Empty);
        }
    }

    private sealed class ConcurrencyTrackingWriter : TextWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();
        private int _activeWrites;
        private int _maximumConcurrency;

        public override Encoding Encoding => Encoding.UTF8;

        public IReadOnlyCollection<string> Lines => _lines;

        public int MaximumConcurrency => _maximumConcurrency;

        public override async Task WriteLineAsync(string? value)
        {
            int activeWrites = Interlocked.Increment(ref _activeWrites);
            InterlockedExtensions.Max(ref _maximumConcurrency, activeWrites);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5));
                _lines.Enqueue(value ?? string.Empty);
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

        public TaskCompletionSource WriteAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task WriteLineAsync(string? value)
        {
            WriteAttempted.TrySetResult();

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
