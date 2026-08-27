// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientConnectionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConcurrentProducersUseSingleStreamWriter()
    {
        ConnectionTestContext context = CreateConnection();
        await using RpcClientConnection connection = context.Connection;
        StreamingMessage[] messages = Enumerable.Range(0, 100)
            .Select(index => new StreamingMessage { RequestId = index.ToString() })
            .ToArray();

        await Task.WhenAll(messages.Select(message => connection.EnqueueAsync(message).AsTask()));
        await WaitUntilAsync(() => context.Channel.WrittenMessages.Count == messages.Length);

        Assert.Equal(1, context.Channel.MaxConcurrentWrites);
        Assert.Equal(messages.Select(message => message.RequestId).OrderBy(value => value),
            context.Channel.WrittenMessages.Select(message => message.RequestId).OrderBy(value => value));
    }

    [Fact]
    public async Task InboundQueueDoesNotDelayPeerClose()
    {
        ConnectionTestContext context = CreateConnection();
        await using RpcClientConnection connection = context.Connection;
        await context.Channel.SendResponseAsync(new StreamingMessage { RequestId = "first" });
        await context.Channel.SendResponseAsync(new StreamingMessage { RequestId = "second" });
        context.Channel.CompleteResponses();

        await connection.Completion.WaitAsync(TestTimeout);

        await using IAsyncEnumerator<StreamingMessage> responses = connection.ReadAllAsync().GetAsyncEnumerator();
        Assert.True(await responses.MoveNextAsync());
        Assert.Equal("first", responses.Current.RequestId);
        Assert.True(await responses.MoveNextAsync());
        Assert.Equal("second", responses.Current.RequestId);
        Assert.False(await responses.MoveNextAsync());
    }

    [Fact]
    public async Task ReaderFailureFaultsConnectionAndDisposesResources()
    {
        ConnectionTestContext context = CreateConnection();
        InvalidOperationException expected = new("read failure");
        context.Channel.CompleteResponses(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Connection.Completion);

        Assert.Same(expected, actual);
        Assert.Equal(1, context.Channel.DisposeCount);
    }

    [Fact]
    public async Task CleanPeerCloseDisposesResourcesExactlyOnce()
    {
        ConnectionTestContext context = CreateConnection();

        context.Channel.CompleteResponses();
        await context.Connection.Completion;
        await context.Connection.DisposeAsync();

        Assert.Equal(1, context.Channel.DisposeCount);
    }

    [Fact]
    public async Task CleanupFailureDoesNotAffectAnotherConnection()
    {
        InvalidOperationException expected = new("cancellation cleanup failure");
        ConnectionTestContext failingContext = CreateConnection(cancellationException: expected);

        AggregateException actual = await Assert.ThrowsAsync<AggregateException>(() =>
            failingContext.Connection.DisposeAsync().AsTask());

        Assert.Same(expected, Assert.Single(actual.InnerExceptions));

        ConnectionTestContext healthyContext = CreateConnection();
        await healthyContext.Connection.DisposeAsync();
        Assert.Equal(1, healthyContext.Channel.DisposeCount);
    }

    [Fact]
    public async Task MultipleCleanupFailuresAreAggregated()
    {
        InvalidOperationException cancellationFailure = new("cancellation failure");
        InvalidOperationException channelFailure = new("channel failure");
        FakeLogger<RpcClientConnection> logger = new();
        ConnectionTestContext context = CreateConnection(cancellationException: cancellationFailure,
            callDisposeException: channelFailure, logger: logger);

        AggregateException actual = await Assert.ThrowsAsync<AggregateException>(() =>
            context.Connection.DisposeAsync().AsTask());
        IReadOnlyCollection<Exception> failures = actual.Flatten().InnerExceptions;

        Assert.Contains(cancellationFailure, failures);
        Assert.Contains(channelFailure, failures);
        Assert.Equal(2, logger.Collector.Count);
    }

    [Fact]
    public async Task DisposalReportsCancellationShapedCleanupFailure()
    {
        OperationCanceledException expected = new("cleanup canceled");
        ConnectionTestContext context = CreateConnection(callDisposeException: expected);

        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            context.Connection.DisposeAsync().AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task WriterFailureFaultsConnectionAndCancelsReader()
    {
        ConnectionTestContext context = CreateConnection(blockWrites: true);
        InvalidOperationException expected = new("write failure");
        await context.Connection.EnqueueAsync(new StreamingMessage { RequestId = "request" });
        await context.Channel.WriteAttempts.ReadAsync().AsTask().WaitAsync(TestTimeout);

        context.Channel.FailWrites(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Connection.Completion);
        Assert.Same(expected, actual);
        Assert.Equal(1, context.Channel.DisposeCount);
    }

    [Fact]
    public async Task CleanRequestBoundaryCloseDuringWriteDoesNotFaultConnection()
    {
        ConnectionTestContext context = CreateConnection(blockWrites: true);
        await context.Connection.EnqueueAsync(new StreamingMessage { RequestId = "request" });
        await context.Channel.WriteAttempts.ReadAsync().AsTask().WaitAsync(TestTimeout);

        context.Channel.CompleteRequests();
        await WaitUntilAsync(() => context.Channel.ActiveWriteCount == 0);

        Assert.False(context.Connection.Completion.IsCompleted);

        context.Channel.CompleteResponses();

        await context.Connection.Completion;
        Assert.Equal(1, context.Channel.DisposeCount);
    }

    [Fact]
    public async Task UnexpectedTransportCancellationIsReportedAsFault()
    {
        ConnectionTestContext context = CreateConnection();
        TaskCanceledException transportCancellation = new("transport canceled");
        context.Channel.CompleteResponses(transportCancellation);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Connection.Completion);

        Assert.Same(transportCancellation, actual.InnerException);
    }

    [Fact]
    public async Task DisposalUnblocksConsumerWaitingForResponse()
    {
        ConnectionTestContext context = CreateConnection();
        await using IAsyncEnumerator<StreamingMessage> responses = context.Connection.ReadAllAsync().GetAsyncEnumerator();
        Task<bool> pendingRead = responses.MoveNextAsync().AsTask();

        await context.Connection.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingRead);
    }

    [Fact]
    public async Task DisposalDoesNotExposeBufferedResponses()
    {
        ConnectionTestContext context = CreateConnection();
        await context.Channel.SendResponseAsync(new StreamingMessage { RequestId = "buffered" });
        await WaitUntilAsync(() => context.Channel.ReadAttemptCount == 2);

        await context.Connection.DisposeAsync();

        await using IAsyncEnumerator<StreamingMessage> responses = context.Connection.ReadAllAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => responses.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task ConcurrentResponseEnumerationIsRejected()
    {
        ConnectionTestContext context = CreateConnection();
        await using IAsyncEnumerator<StreamingMessage> first = context.Connection.ReadAllAsync().GetAsyncEnumerator();
        Task<bool> pendingRead = first.MoveNextAsync().AsTask();
        await using IAsyncEnumerator<StreamingMessage> second = context.Connection.ReadAllAsync().GetAsyncEnumerator();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            second.MoveNextAsync().AsTask());

        Assert.Equal("Only one response enumeration may be active at a time.", exception.Message);
        await context.Connection.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingRead);
    }

    [Fact]
    public async Task ReaderFailureSurfacesToPendingResponseRead()
    {
        ConnectionTestContext context = CreateConnection();
        await using IAsyncEnumerator<StreamingMessage> responses = context.Connection.ReadAllAsync().GetAsyncEnumerator();
        Task<bool> pendingRead = responses.MoveNextAsync().AsTask();
        InvalidOperationException expected = new("read failure");

        context.Channel.CompleteResponses(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => pendingRead);
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task CancelingResponseEnumerationDoesNotTerminateConnection()
    {
        ConnectionTestContext context = CreateConnection();
        await using RpcClientConnection connection = context.Connection;
        using CancellationTokenSource cancellationSource = new();
        await using IAsyncEnumerator<StreamingMessage> responses = connection.ReadAllAsync(cancellationSource.Token).GetAsyncEnumerator();
        Task<bool> pendingRead = responses.MoveNextAsync().AsTask();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
        Assert.False(connection.Completion.IsCompleted);
    }

    [Fact]
    public async Task RepeatedConcurrentDisposalIsSafe()
    {
        ConnectionTestContext context = CreateConnection();

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => context.Connection.DisposeAsync().AsTask()));

        Assert.Equal(1, context.Channel.DisposeCount);
        Assert.True(context.Connection.Completion.IsCanceled);
    }

    [Fact]
    public async Task LateWriteSurfacesTransportFailure()
    {
        ConnectionTestContext context = CreateConnection();
        InvalidOperationException expected = new("read failure");
        context.Channel.CompleteResponses(expected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Connection.Completion);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Connection.EnqueueAsync(new StreamingMessage()).AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task FaultedConnectionLogsAndReportsCleanupFailureDuringDisposal()
    {
        InvalidOperationException transportFailure = new("transport failure");
        InvalidOperationException cleanupFailure = new("cleanup failure");
        FakeLogger<RpcClientConnection> logger = new();
        ConnectionTestContext context = CreateConnection(callDisposeException: cleanupFailure, logger: logger);
        context.Channel.CompleteResponses(transportFailure);

        InvalidOperationException completionFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Connection.Completion);
        InvalidOperationException disposeFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Connection.DisposeAsync().AsTask());

        Assert.Same(transportFailure, completionFailure);
        Assert.Same(cleanupFailure, disposeFailure);
        Assert.Equal(LogLevel.Warning, logger.Collector.LatestRecord.Level);
        Assert.Same(cleanupFailure, logger.Collector.LatestRecord.Exception);
        Assert.Contains("disposing the duplex channel", logger.Collector.LatestRecord.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnqueueAfterCleanPeerCloseReportsCompletedConnection()
    {
        ConnectionTestContext context = CreateConnection();
        context.Channel.CompleteResponses();
        await context.Connection.Completion;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Connection.EnqueueAsync(new StreamingMessage()).AsTask());
    }

    [Fact]
    public async Task EnqueueAfterDisposalReportsDisposedConnection()
    {
        ConnectionTestContext context = CreateConnection();
        await context.Connection.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            context.Connection.EnqueueAsync(new StreamingMessage()).AsTask());
    }

    private static ConnectionTestContext CreateConnection(bool blockWrites = false, Exception cancellationException = null,
        Exception callDisposeException = null, ILogger<RpcClientConnection> logger = null)
    {
        CancellationTokenSource shutdownSource = new();
        if (cancellationException is not null)
        {
            shutdownSource.Token.Register(() => throw cancellationException);
        }

        TestDuplexChannel<StreamingMessage> channel = new(blockWrites, callDisposeException);
        RpcClientConnection connection = new("worker-1", channel, logger ?? NullLogger<RpcClientConnection>.Instance, shutdownSource);
        return new ConnectionTestContext(connection, channel);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string conditionExpression = null)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        try
        {
            while (!condition())
            {
                await Task.Delay(10, timeoutSource.Token);
            }
        }
        catch (OperationCanceledException exception) when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"Condition '{conditionExpression}' was not met within {TestTimeout}.", exception);
        }
    }

    private sealed record ConnectionTestContext(RpcClientConnection Connection, TestDuplexChannel<StreamingMessage> Channel);
}
