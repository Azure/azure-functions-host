// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics;

public class BufferedConsoleWriterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public async Task FlushAsync_DrainsPrecedingLinesAndKeepsWriterRunning(int bufferSize)
    {
        StringWriter output = new();
        using BufferedConsoleWriter writer = new(bufferSize, exception => throw exception,
            TimeSpan.FromSeconds(1), autoStart: false) { Writer = output };
        writer.WriteHandler("first");
        Task flush = writer.FlushAsync();
        Assert.False(flush.IsCompleted);

        writer.StartProcessingBuffer();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal($"first{Environment.NewLine}", output.ToString());

        writer.WriteHandler("second");
        await writer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", output.ToString());
        await writer.CompleteAsync();
        await writer.FlushAsync();
    }

    [Fact]
    public async Task FlushAsync_PreservesBatchesAcrossConcurrentFlushes()
    {
        StringWriter output = new();
        using BufferedConsoleWriter writer = new(100, exception => throw exception,
            TimeSpan.FromSeconds(1), autoStart: false) { Writer = output };
        writer.WriteHandler("first");
        writer.WriteHandler("second");
        Task firstFlush = writer.FlushAsync();
        Task secondFlush = writer.FlushAsync();
        writer.WriteHandler("third");
        writer.StartProcessingBuffer();

        await Task.WhenAll(firstFlush, secondFlush).WaitAsync(TimeSpan.FromSeconds(5));
        await writer.FlushAsync();

        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}third{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task FlushAsync_CancellationDoesNotStopWriter()
    {
        StringWriter output = new();
        using BufferedConsoleWriter writer = new(100, exception => throw exception,
            TimeSpan.FromSeconds(1), autoStart: false) { Writer = output };
        using CancellationTokenSource cancellation = new();
        writer.WriteHandler("first");
        Task flush = writer.FlushAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);

        writer.StartProcessingBuffer();
        writer.WriteHandler("second");
        await writer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task FlushAsync_ReportsWriterFailureToAllWaiters()
    {
        IOException failure = new("Console unavailable.");
        Mock<TextWriter> output = new();
        output.Setup(writer => writer.WriteLineAsync(It.IsAny<string>())).ThrowsAsync(failure);
        TaskCompletionSource<Exception> observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using BufferedConsoleWriter writer = new(100, exception => observed.TrySetResult(exception),
            TimeSpan.FromSeconds(1), autoStart: false) { Writer = output.Object };
        writer.WriteHandler("first");
        Task firstFlush = writer.FlushAsync();
        Task secondFlush = writer.FlushAsync();

        writer.StartProcessingBuffer();

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => firstFlush.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => secondFlush.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Same(failure, await observed.Task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.FlushAsync());
    }
}
