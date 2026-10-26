// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class GrpcDuplexChannelTests
{
    [Fact]
    public async Task HttpProxy_PeerReceivesTerminalBeforeWriteCompletes_NextRequestDoesNotFaultTransport()
    {
        MockDuplexStream<StreamingMessage> streams = new(blockWrites: true);
        HttpProxyOptions options = new() { Enabled = true, MaxPendingRequests = 1 };
        TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Mock<IHttpProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    secondEntered.TrySetResult();
                }

                return Task.FromResult(new HttpProxyResponse { StatusCode = 200 });
            });
        await using HttpProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, new HttpProxyAdmission(32, options.MaxHostBufferedBytes), NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        StreamingMessage hello = await streams.WriteAttempts.ReadAsync(timeout.Token);
        string sessionId = hello.HttpProxy.SessionId;
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                Ready = new() { Version = HttpProxyProtocol.Version, Limits = options.ToLimits() }
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

    [Theory]
    [InlineData(199, 0)]
    [InlineData(600, 0)]
    [InlineData(200, -1)]
    public async Task HttpProxy_InvalidHandlerResponse_ProducesTerminalFailureWithoutClosingWorkerTraffic(int status, long length)
    {
        MockDuplexStream<StreamingMessage> streams = new();
        HttpProxyOptions options = new() { Enabled = true };
        Mock<IHttpProxyRequestHandler> handler = new();
        handler.Setup(value => value.SendAsync(It.IsAny<HttpProxyRequest>(), It.IsAny<HttpProxyLimits>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpProxyResponse { StatusCode = (uint)status, ContentLength = length });
        await using HttpProxyDuplexChannel transport = new(streams.Call.AsChannel(), handler.Object, options,
            TimeProvider.System, new HttpProxyAdmission(32, options.MaxHostBufferedBytes), NullLogger.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        StreamingMessage hello = await streams.WriteAttempts.ReadAsync(timeout.Token);
        string sessionId = hello.HttpProxy.SessionId;
        await streams.SendResponseAsync(new()
        {
            HttpProxy = new()
            {
                SessionId = sessionId,
                Ready = new() { Version = HttpProxyProtocol.Version, Limits = options.ToLimits() }
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
}
