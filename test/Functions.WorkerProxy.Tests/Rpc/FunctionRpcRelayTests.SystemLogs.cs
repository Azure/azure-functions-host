// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task Relay_SystemLogDisabledForwardsWithoutEmitting()
    {
        Mock<IWorkerSystemLogSink> sink = CreateSystemLogSink();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLogFactory(WorkerSystemLogMode.Disabled, sink);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await AttachWorkerAsync(runtime, worker, timeout.Token);
        StreamingMessage message = CreateSystemLog("disabled");

        await worker.WriteAsync(message, timeout.Token);

        Assert.Equal(message, await runtime.ReadAsync(timeout.Token));
        sink.Verify(instance => instance.EmitAsync(It.IsAny<RpcLog>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Relay_SystemLogMirrorEmitsAndForwards()
    {
        Mock<IWorkerSystemLogSink> sink = CreateSystemLogSink();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLogFactory(WorkerSystemLogMode.Mirror, sink);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await AttachWorkerAsync(runtime, worker, timeout.Token);
        StreamingMessage message = CreateSystemLog("mirror");

        await worker.WriteAsync(message, timeout.Token);

        Assert.Equal(message, await runtime.ReadAsync(timeout.Token));
        sink.Verify(instance => instance.EmitAsync(message.RpcLog, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Relay_SystemLogConsumeEmitsAndContinuesWithNextMessage()
    {
        Mock<IWorkerSystemLogSink> sink = CreateSystemLogSink();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLogFactory(WorkerSystemLogMode.Consume, sink);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await AttachWorkerAsync(runtime, worker, timeout.Token);
        StreamingMessage consumed = CreateSystemLog("consume");
        StreamingMessage following = CreateMessage("following");

        await worker.WriteAsync(consumed, timeout.Token);
        await worker.WriteAsync(following, timeout.Token);

        Assert.Equal(following, await runtime.ReadAsync(timeout.Token));
        sink.Verify(instance => instance.EmitAsync(consumed.RpcLog, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Relay_SystemLogEmissionFailureForwardsOriginalMessage()
    {
        Mock<IWorkerSystemLogSink> sink = CreateSystemLogSink();
        sink.Setup(instance => instance.EmitAsync(It.IsAny<RpcLog>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromException(new WorkerSystemLogEmissionException(
                "Injected failure.",
                new IOException("Injected output failure."))));
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLogFactory(WorkerSystemLogMode.Consume, sink);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await AttachWorkerAsync(runtime, worker, timeout.Token);
        StreamingMessage message = CreateSystemLog("failure");

        await worker.WriteAsync(message, timeout.Token);

        Assert.Equal(message, await runtime.ReadAsync(timeout.Token));
    }

    [Theory]
    [InlineData(RpcLog.Types.RpcLogCategory.User)]
    [InlineData(RpcLog.Types.RpcLogCategory.CustomMetric)]
    public async Task Relay_NonSystemRpcLogsAreNeverIntercepted(RpcLog.Types.RpcLogCategory category)
    {
        Mock<IWorkerSystemLogSink> sink = CreateSystemLogSink();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLogFactory(WorkerSystemLogMode.Consume, sink);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await AttachWorkerAsync(runtime, worker, timeout.Token);
        StreamingMessage message = CreateSystemLog("non-system");
        message.RpcLog.LogCategory = category;

        await worker.WriteAsync(message, timeout.Token);

        Assert.Equal(message, await runtime.ReadAsync(timeout.Token));
        sink.Verify(instance => instance.EmitAsync(It.IsAny<RpcLog>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Relay_RuntimeSystemLogIsNeverIntercepted()
    {
        Mock<IWorkerSystemLogSink> sink = CreateSystemLogSink();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLogFactory(WorkerSystemLogMode.Consume, sink);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await AttachWorkerAsync(runtime, worker, timeout.Token);
        StreamingMessage message = CreateSystemLog("runtime");

        await runtime.WriteAsync(message, timeout.Token);

        Assert.Equal(message, await worker.ReadAsync(timeout.Token));
        sink.Verify(instance => instance.EmitAsync(It.IsAny<RpcLog>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static Mock<IWorkerSystemLogSink> CreateSystemLogSink()
    {
        Mock<IWorkerSystemLogSink> sink = new();
        sink.Setup(instance => instance.EmitAsync(It.IsAny<RpcLog>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        return sink;
    }

    private static WorkerProxyWebApplicationFactory CreateSystemLogFactory(
        WorkerSystemLogMode mode,
        Mock<IWorkerSystemLogSink> sink)
    {
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.SystemLogMode)}"] = mode.ToString()
        };

        return new WorkerProxyWebApplicationFactory(configuration, services =>
        {
            services.RemoveAll<IWorkerSystemLogSink>();
            services.AddSingleton(sink.Object);
        });
    }

    private static async Task AttachWorkerAsync(
        RelayClient runtime,
        RelayClient worker,
        CancellationToken cancellationToken)
    {
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, cancellationToken);
        Assert.Equal(start, await runtime.ReadAsync(cancellationToken));
    }

    private static StreamingMessage CreateSystemLog(string requestId)
    {
        return new StreamingMessage
        {
            RequestId = requestId,
            RpcLog = new RpcLog
            {
                LogCategory = RpcLog.Types.RpcLogCategory.System,
                Level = RpcLog.Types.Level.Information,
                Message = requestId
            }
        };
    }
}
