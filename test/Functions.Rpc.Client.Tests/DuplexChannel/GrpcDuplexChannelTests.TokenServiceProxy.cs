// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class GrpcDuplexChannelTests
{
    [Fact]
    public async Task TokenServiceProxy_PeerReceivesTerminalBeforeWriteCompletes_NextRequestDoesNotFaultTransport()
    {
        MockDuplexStream<StreamingMessage> streams = new(blockWrites: true);
        TokenServiceProxyOptions options = new() { Enabled = true, MaxPendingRequests = 2 };
        TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    secondEntered.TrySetResult();
                }

                return Task.FromResult(new HttpProxyResponse { StatusCode = 200 });
            });
        await using TokenServiceProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, new TokenServiceProxyAdmission(32, options.MaxHostBufferedBytes), NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        StreamingMessage hello = await streams.WriteAttempts.ReadAsync(timeout.Token);
        string sessionId = hello.HttpProxy.SessionId;
        HttpProxyLimits peerLimits = options.ToLimits();
        peerLimits.MaxPendingRequests = 1;
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                Ready = new() { Version = TokenServiceProxyProtocol.Version, Limits = peerLimits }
            }
        });
        streams.AllowWrite();
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                RequestId = Guid.NewGuid().ToString("N"),
                Request = new() { Method = "GET", Target = "/MSI/token", TimeoutMs = 1000 }
            }
        });
        StreamingMessage firstTerminal = await streams.WriteAttempts.ReadAsync(timeout.Token);
        Assert.NotNull(firstTerminal.HttpProxy.Response);
        string secondId = Guid.NewGuid().ToString("N");
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                RequestId = secondId,
                Request = new() { Method = "GET", Target = "/MSI/token", TimeoutMs = 1000 }
            }
        });

        await secondEntered.Task.WaitAsync(timeout.Token);

        Assert.False(transport.Reader.Completion.IsCompleted);
        streams.AllowWrite();
        StreamingMessage secondTerminal = await streams.WriteAttempts.ReadAsync(timeout.Token);
        Assert.Equal(secondId, secondTerminal.HttpProxy.RequestId);
        Assert.NotNull(secondTerminal.HttpProxy.Response);
        streams.AllowWrite();
    }

    [Fact]
    public async Task TokenServiceProxy_TransportByteBudgetRejectsExcessWork_AndRecoversAfterCompletion()
    {
        MockDuplexStream<StreamingMessage> streams = new();
        TokenServiceProxyOptions options = new() { Enabled = true };
        options.MaxBufferedBytes = options.ReservationBytes;
        TokenServiceProxyAdmission global = new(32, options.MaxHostBufferedBytes);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(async (HttpProxyRequest request, HttpProxyLimits _, CancellationToken token) =>
            {
                if (string.Equals(request.Target, "/first", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }

                return new HttpProxyResponse { StatusCode = 200 };
            });
        await using TokenServiceProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, global, NullLogger.Instance);
        MockDuplexStream<StreamingMessage> otherStreams = new();
        await using TokenServiceProxyDuplexChannel other = new(otherStreams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, global, NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string sessionId = await InitializeHttpProxyAsync(streams, options, timeout.Token);
        string otherSessionId = await InitializeHttpProxyAsync(otherStreams, options, timeout.Token);
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/first"));
        await entered.Task.WaitAsync(timeout.Token);
        StreamingMessage excess = CreateHttpProxyRequest(sessionId, "/excess");
        await streams.SendResponseAsync(excess);

        StreamingMessage rejected = await streams.WriteAttempts.ReadAsync(timeout.Token);

        Assert.Equal(excess.HttpProxy.RequestId, rejected.HttpProxy.RequestId);
        Assert.Equal(HttpProxyFailure.Types.Code.TooManyRequests, rejected.HttpProxy.Failure.Code);
        await otherStreams.SendResponseAsync(CreateHttpProxyRequest(otherSessionId, "/independent"));
        Assert.NotNull((await otherStreams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
        release.SetResult();
        Assert.NotNull((await streams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
        await transport.WaitForPendingOperationsAsync().WaitAsync(timeout.Token);
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/recovered"));
        Assert.NotNull((await streams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
        handler.Verify(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenServiceProxy_GlobalBudgetIsSharedAcrossTransports(bool limitBytes)
    {
        TokenServiceProxyOptions options = new() { Enabled = true };
        TokenServiceProxyAdmission global = new(limitBytes ? 32 : 1, limitBytes ? options.ReservationBytes : options.MaxHostBufferedBytes);
        MockDuplexStream<StreamingMessage> firstStreams = new();
        MockDuplexStream<StreamingMessage> secondStreams = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(async (HttpProxyRequest _, HttpProxyLimits _, CancellationToken token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return new HttpProxyResponse { StatusCode = 200 };
            });
        await using TokenServiceProxyDuplexChannel first = new(firstStreams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, global, NullLogger.Instance);
        await using TokenServiceProxyDuplexChannel second = new(secondStreams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, global, NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string firstSession = await InitializeHttpProxyAsync(firstStreams, options, timeout.Token);
        string secondSession = await InitializeHttpProxyAsync(secondStreams, options, timeout.Token);
        await firstStreams.SendResponseAsync(CreateHttpProxyRequest(firstSession, "/first"));
        await entered.Task.WaitAsync(timeout.Token);
        await secondStreams.SendResponseAsync(CreateHttpProxyRequest(secondSession, "/second"));

        StreamingMessage rejected = await secondStreams.WriteAttempts.ReadAsync(timeout.Token);

        Assert.Equal(HttpProxyFailure.Types.Code.TooManyRequests, rejected.HttpProxy.Failure.Code);
        release.SetResult();
        await firstStreams.WriteAttempts.ReadAsync(timeout.Token);
        await first.WaitForPendingOperationsAsync().WaitAsync(timeout.Token);
        await second.WaitForPendingOperationsAsync().WaitAsync(timeout.Token);
        await secondStreams.SendResponseAsync(CreateHttpProxyRequest(secondSession, "/recovered"));
        Assert.NotNull((await secondStreams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenServiceProxy_TransportBudgetRemainsReservedUntilTerminalWriteCompletes(bool limitBytes)
    {
        MockDuplexStream<StreamingMessage> streams = new(blockWrites: true);
        TokenServiceProxyOptions options = new() { Enabled = true, MaxPendingRequests = limitBytes ? 32 : 1 };
        if (limitBytes)
        {
            options.MaxBufferedBytes = options.ReservationBytes;
        }

        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new HttpProxyResponse { StatusCode = 200 }));
        await using TokenServiceProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, new TokenServiceProxyAdmission(32, options.MaxHostBufferedBytes), NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string sessionId = await InitializeHttpProxyAsync(streams, options, timeout.Token);
        streams.AllowWrite();
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/first"));
        Assert.NotNull((await streams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/excess"));
        await streams.SendResponseAsync(new() { WorkerHeartbeat = new() });
        await transport.Reader.ReadAsync(timeout.Token);
        handler.Verify(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()),
            Times.Once);

        streams.AllowWrite();
        StreamingMessage rejected = await streams.WriteAttempts.ReadAsync(timeout.Token);

        Assert.Equal(HttpProxyFailure.Types.Code.TooManyRequests, rejected.HttpProxy.Failure.Code);
        Assert.False(transport.Reader.Completion.IsCompleted);
        streams.AllowWrite();
        await transport.WaitForPendingOperationsAsync().WaitAsync(timeout.Token);
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/recovered"));
        Assert.NotNull((await streams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
        streams.AllowWrite();
    }

    [Fact]
    public async Task TokenServiceProxy_CancelledRequestReleasesBothBudgetsAfterItsTerminalOutcome()
    {
        MockDuplexStream<StreamingMessage> streams = new();
        TokenServiceProxyOptions options = new() { Enabled = true };
        options.MaxBufferedBytes = options.ReservationBytes;
        TokenServiceProxyAdmission global = new(1, options.ReservationBytes);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(async (HttpProxyRequest request, HttpProxyLimits _, CancellationToken token) =>
            {
                if (string.Equals(request.Target, "/cancel", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }

                return new HttpProxyResponse { StatusCode = 200 };
            });
        await using TokenServiceProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, global, NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string sessionId = await InitializeHttpProxyAsync(streams, options, timeout.Token);
        StreamingMessage request = CreateHttpProxyRequest(sessionId, "/cancel");
        await streams.SendResponseAsync(request);
        await entered.Task.WaitAsync(timeout.Token);
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new() { SessionId = sessionId, RequestId = request.HttpProxy.RequestId, Cancel = new() }
        });

        StreamingMessage terminal = await streams.WriteAttempts.ReadAsync(timeout.Token);

        Assert.Equal(HttpProxyFailure.Types.Code.Cancelled, terminal.HttpProxy.Failure.Code);
        await transport.WaitForPendingOperationsAsync().WaitAsync(timeout.Token);
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/recovered"));
        Assert.NotNull((await streams.WriteAttempts.ReadAsync(timeout.Token)).HttpProxy.Response);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenServiceProxy_QueuedTerminalResponsesHoldBudgetsUntilWriteOrDiscard(bool failWrite)
    {
        MockDuplexStream<StreamingMessage> streams = new(blockWrites: true);
        TokenServiceProxyOptions options = new() { Enabled = true };
        options.MaxBufferedBytes = 2 * options.ReservationBytes;
        TokenServiceProxyAdmission global = new(2, 2 * options.ReservationBytes);
        GrpcDuplexChannel<StreamingMessage> inner = streams.Call.AsChannel();
        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new HttpProxyResponse { StatusCode = 200, Body = ByteString.CopyFrom(new byte[128]) }));
        await using TokenServiceProxyDuplexChannel transport = new(inner, handler.Object, options, TimeProvider.System, global, NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        string sessionId = await InitializeHttpProxyAsync(streams, options, timeout.Token);
        streams.AllowWrite();
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/first"));
        await streams.WriteAttempts.ReadAsync(timeout.Token);
        await streams.SendResponseAsync(CreateHttpProxyRequest(sessionId, "/second"));
        await streams.SendResponseAsync(new() { WorkerHeartbeat = new() });
        await transport.Reader.ReadAsync(timeout.Token);
        Assert.True(inner.HasQueuedWrites);
        Assert.Null(global.TryAcquire(options.ReservationBytes));

        if (failWrite)
        {
            streams.FailWrites(new IOException("write failed"));
            await Assert.ThrowsAsync<IOException>(() => transport.Reader.Completion.WaitAsync(timeout.Token));
        }
        else
        {
            await transport.DisposeAsync();
        }

        await transport.WaitForPendingOperationsAsync().WaitAsync(timeout.Token);
        Assert.False(inner.HasQueuedWrites);
        using TokenServiceProxyAdmission.Lease firstRecovered = global.TryAcquire(options.ReservationBytes);
        using TokenServiceProxyAdmission.Lease secondRecovered = global.TryAcquire(options.ReservationBytes);
        Assert.NotNull(firstRecovered);
        Assert.NotNull(secondRecovered);
    }

    [Theory]
    [InlineData(199, 0)]
    [InlineData(600, 0)]
    [InlineData(200, -1)]
    public async Task TokenServiceProxy_InvalidHandlerResponse_ProducesTerminalFailureWithoutClosingWorkerTraffic(int status, long length)
    {
        MockDuplexStream<StreamingMessage> streams = new();
        TokenServiceProxyOptions options = new() { Enabled = true };
        Mock<ITokenServiceProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpProxyResponse { StatusCode = (uint)status, ContentLength = length });
        await using TokenServiceProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, new TokenServiceProxyAdmission(32, options.MaxHostBufferedBytes), NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        StreamingMessage hello = await streams.WriteAttempts.ReadAsync(timeout.Token);
        string sessionId = hello.HttpProxy.SessionId;
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                Ready = new() { Version = TokenServiceProxyProtocol.Version, Limits = options.ToLimits() }
            }
        });
        string requestId = Guid.NewGuid().ToString("N");
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                RequestId = requestId,
                Request = new() { Method = "GET", Target = "/MSI/token", TimeoutMs = 1000 }
            }
        });

        StreamingMessage terminal = await streams.WriteAttempts.ReadAsync(timeout.Token);

        Assert.Equal(requestId, terminal.HttpProxy.RequestId);
        Assert.Equal(HttpProxyFailure.Types.Code.BadGateway, terminal.HttpProxy.Failure.Code);
        StreamingMessage heartbeat = new() { WorkerHeartbeat = new() };
        await streams.SendResponseAsync(heartbeat);
        Assert.Same(heartbeat, await transport.Reader.ReadAsync(timeout.Token));
    }

    private static async Task<string> InitializeHttpProxyAsync(MockDuplexStream<StreamingMessage> streams,
        TokenServiceProxyOptions options, CancellationToken cancellationToken)
    {
        StreamingMessage hello = await streams.WriteAttempts.ReadAsync(cancellationToken);
        string sessionId = hello.HttpProxy.SessionId;
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                Ready = new() { Version = TokenServiceProxyProtocol.Version, Limits = options.ToLimits() }
            }
        });

        return sessionId;
    }

    private static StreamingMessage CreateHttpProxyRequest(string sessionId, string target) => new()
    {
        HttpProxy = new()
        {
            SessionId = sessionId,
            RequestId = Guid.NewGuid().ToString("N"),
            Request = new() { Method = "GET", Target = target, TimeoutMs = 10000 }
        }
    };
}
