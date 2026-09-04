// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers;

public class ExtensionRpcStreamCoordinatorTests
{
    [Fact]
    public async Task RunAsync_ReconnectsFailedStream()
    {
        var openedStreams = Channel.CreateUnbounded<TestStream>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var coordinator = new ExtensionRpcStreamCoordinator(
            "worker-1",
            cancellationToken => OpenTestStream(openedStreams, cancellationToken),
            new TestEndpointRouter(new ExtensionRpcEndpoint(_ => Task.CompletedTask, services)),
            NullLogger.Instance,
            CancellationToken.None);

        Task coordinatorTask = coordinator.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        TestStream failedStream = await openedStreams.Reader.ReadAsync(timeout.Token);

        failedStream.Inbound.Writer.TryComplete();

        TestStream replacementStream = await openedStreams.Reader.ReadAsync(timeout.Token);
        Assert.NotSame(failedStream, replacementStream);
        Assert.Equal(1, coordinator.ActiveStreamCount);

        await coordinator.DisposeAsync();
        await coordinatorTask;
    }

    [Fact]
    public async Task RunAsync_KeepsOneStreamOpenUnderLoad()
    {
        var openedStreams = Channel.CreateUnbounded<TestStream>();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            context => Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted),
            services);
        var coordinator = new ExtensionRpcStreamCoordinator(
            "worker-1",
            cancellationToken => OpenTestStream(openedStreams, cancellationToken),
            new TestEndpointRouter(endpoint),
            NullLogger.Instance,
            CancellationToken.None);

        Task coordinatorTask = coordinator.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        TestStream stream = await openedStreams.Reader.ReadAsync(timeout.Token);
        const string sessionId = "session-1";
        const string shardId = "shard-1";
        await stream.Inbound.Writer.WriteAsync(CreateHello(sessionId, shardId), timeout.Token);
        ExtensionRpcMessage ready = await stream.Outbound.Reader.ReadAsync(timeout.Token);
        Assert.True(ready.Ready.Enabled);

        for (int i = 0; i < 64; i++)
        {
            await stream.Inbound.Writer.WriteAsync(
                new ExtensionRpcMessage
                {
                    SessionId = sessionId,
                    ShardId = shardId,
                    CallId = $"call-{i}",
                    Start = new ExtensionRpcStart
                    {
                        Method = "/extensions.Echo/Bidirectional",
                    },
                },
                timeout.Token);
        }

        await Task.Delay(ExtensionRpcStreamCoordinator.ReconnectDelay * 2, timeout.Token);
        Assert.False(openedStreams.Reader.TryRead(out _));
        Assert.Equal(1, coordinator.ActiveStreamCount);

        await coordinator.DisposeAsync();
        await coordinatorTask;
    }

    [Fact]
    public async Task RunAsync_ReconnectsWhenEndpointIgnoresCancellation()
    {
        var openedStreams = Channel.CreateUnbounded<TestStream>();
        var endpointStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpointCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var endpoint = new ExtensionRpcEndpoint(
            _ =>
            {
                endpointStarted.TrySetResult();
                return endpointCompletion.Task;
            },
            services);
        var coordinator = new ExtensionRpcStreamCoordinator(
            "worker-1",
            cancellationToken => OpenTestStream(openedStreams, cancellationToken),
            new TestEndpointRouter(endpoint),
            NullLogger.Instance,
            CancellationToken.None);

        Task coordinatorTask = coordinator.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        TestStream failedStream = await openedStreams.Reader.ReadAsync(timeout.Token);
        const string sessionId = "session-1";
        const string shardId = "shard-1";
        await failedStream.Inbound.Writer.WriteAsync(CreateHello(sessionId, shardId), timeout.Token);
        await failedStream.Outbound.Reader.ReadAsync(timeout.Token);
        await failedStream.Inbound.Writer.WriteAsync(
            new ExtensionRpcMessage
            {
                SessionId = sessionId,
                ShardId = shardId,
                CallId = "call-1",
                Start = new ExtensionRpcStart
                {
                    Method = "/extensions.Echo/Bidirectional",
                },
            },
            timeout.Token);
        await endpointStarted.Task.WaitAsync(timeout.Token);

        failedStream.Inbound.Writer.TryComplete();

        TestStream replacementStream = await openedStreams.Reader.ReadAsync(timeout.Token);
        Assert.NotSame(failedStream, replacementStream);
        Assert.Equal(1, coordinator.ActiveStreamCount);

        endpointCompletion.TrySetResult();
        await coordinator.DisposeAsync();
        await coordinatorTask;
    }

    private static AsyncDuplexStreamingCall<ExtensionRpcMessage, ExtensionRpcMessage> OpenTestStream(
        Channel<TestStream> openedStreams,
        CancellationToken cancellationToken)
    {
        var stream = new TestStream();
        openedStreams.Writer.TryWrite(stream);
        return new AsyncDuplexStreamingCall<ExtensionRpcMessage, ExtensionRpcMessage>(
            new TestClientStreamWriter<ExtensionRpcMessage>(stream.Outbound.Writer),
            new TestAsyncStreamReader<ExtensionRpcMessage>(stream.Inbound.Reader),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    private static ExtensionRpcMessage CreateHello(string sessionId, string shardId)
    {
        return new ExtensionRpcMessage
        {
            SessionId = sessionId,
            ShardId = shardId,
            Hello = new ExtensionRpcHello
            {
                SupportedVersions = { ExtensionRpcStreamDispatcher.ProtocolVersion },
                InitialReceiveWindowBytes = ExtensionRpcStreamDispatcher.DefaultInitialWindowSize,
                MaxDataChunkBytes = ExtensionRpcStreamDispatcher.DefaultMaxChunkSize,
                MaxMessageBytes = ExtensionRpcStreamDispatcher.DefaultMaxMessageSize,
            },
        };
    }

    private sealed class TestStream
    {
        public Channel<ExtensionRpcMessage> Inbound { get; } = Channel.CreateUnbounded<ExtensionRpcMessage>();

        public Channel<ExtensionRpcMessage> Outbound { get; } = Channel.CreateUnbounded<ExtensionRpcMessage>();
    }

    private sealed class TestEndpointRouter(ExtensionRpcEndpoint endpoint) : IExtensionRpcEndpointRouter
    {
        public ValueTask<ExtensionRpcEndpoint?> RouteAsync(
            string workerId,
            string method,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<ExtensionRpcEndpoint?>(endpoint);
        }
    }

    private sealed class TestClientStreamWriter<T>(ChannelWriter<T> writer) : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync()
        {
            writer.TryComplete();
            return Task.CompletedTask;
        }

        public Task WriteAsync(T message)
        {
            return writer.WriteAsync(message).AsTask();
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken)
        {
            return writer.WriteAsync(message, cancellationToken).AsTask();
        }
    }

    private sealed class TestAsyncStreamReader<T>(ChannelReader<T> reader) : IAsyncStreamReader<T>
    {
        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!await reader.WaitToReadAsync(cancellationToken) || !reader.TryRead(out T? item))
            {
                return false;
            }

            Current = item;
            return true;
        }
    }
}
