// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_InterceptorReceivesOriginatingSideAndForwardedMessage(bool runtimeOrigin)
    {
        FunctionRpcRelaySide origin = runtimeOrigin ? FunctionRpcRelaySide.Runtime : FunctionRpcRelaySide.Worker;
        TestFunctionRpcMessageInterceptor interceptor = new();
        await using WorkerProxyWebApplicationFactory factory = CreateFactory(interceptor);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        RelayClient source = origin == FunctionRpcRelaySide.Runtime ? runtime : worker;
        RelayClient destination = origin == FunctionRpcRelaySide.Runtime ? worker : runtime;
        StreamingMessage message = CreateMessage($"{origin}-message");

        await source.WriteAsync(message, timeout.Token);

        FunctionRpcMessageObservation observation = await interceptor.ReadAsync(timeout.Token);
        Assert.Equal(origin, observation.Side);
        Assert.Equal(message, observation.Message);
        Assert.Equal(message, await destination.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task Relay_ConsumedMessageDoesNotPreventLaterForwarding()
    {
        TestFunctionRpcMessageInterceptor interceptor = new(static message =>
            string.Equals(message.RequestId, "consumed", StringComparison.Ordinal)
                ? FunctionRpcMessageDisposition.Consumed
                : FunctionRpcMessageDisposition.Forward);
        await using WorkerProxyWebApplicationFactory factory = CreateFactory(interceptor);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage forwarded = CreateMessage("forwarded");

        await worker.WriteAsync(CreateMessage("consumed"), timeout.Token);
        await worker.WriteAsync(forwarded, timeout.Token);

        Assert.Equal(forwarded, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(2, interceptor.Count);
    }

    [Fact]
    public async Task Relay_InterceptorFailureUsesExistingFaultTermination()
    {
        InvalidOperationException expected = new("Injected interceptor failure.");
        TestFunctionRpcMessageInterceptor interceptor = new(_ => throw expected);
        await using WorkerProxyWebApplicationFactory factory = CreateFactory(interceptor);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);

        await worker.WriteAsync(CreateMessage("fault"), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, relay.LastTerminalState?.Reason);
        Assert.Same(expected, relay.LastTerminalState?.Exception);
        Assert.Equal(FunctionRpcRelaySide.Worker, relay.LastTerminalState?.Side);
    }

    [Fact]
    public async Task Relay_DefaultInterceptorForwardsRpcLogUnchanged()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        Assert.IsType<PassThroughFunctionRpcMessageInterceptor>(
            factory.Services.GetRequiredService<IFunctionRpcMessageInterceptor>());
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage rpcLog = CreateMessage("rpc-log");

        await worker.WriteAsync(rpcLog, timeout.Token);

        Assert.Equal(rpcLog, await runtime.ReadAsync(timeout.Token));
    }

    private static WorkerProxyWebApplicationFactory CreateFactory(IFunctionRpcMessageInterceptor interceptor)
    {
        return new WorkerProxyWebApplicationFactory(
            configureServices: services => services.AddSingleton<IFunctionRpcMessageInterceptor>(interceptor));
    }

    private sealed class TestFunctionRpcMessageInterceptor : IFunctionRpcMessageInterceptor
    {
        private readonly Func<StreamingMessage, FunctionRpcMessageDisposition> _getDisposition;
        private readonly Channel<FunctionRpcMessageObservation> _observations = Channel.CreateUnbounded<FunctionRpcMessageObservation>();
        private int _count;

        public TestFunctionRpcMessageInterceptor(
            Func<StreamingMessage, FunctionRpcMessageDisposition>? getDisposition = null)
        {
            _getDisposition = getDisposition ?? (_ => FunctionRpcMessageDisposition.Forward);
        }

        public int Count => Volatile.Read(ref _count);

        public ValueTask<FunctionRpcMessageObservation> ReadAsync(CancellationToken cancellationToken)
        {
            return _observations.Reader.ReadAsync(cancellationToken);
        }

        public ValueTask<FunctionRpcMessageDisposition> ProcessAsync(
            FunctionRpcRelaySide side,
            StreamingMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _count);
            _observations.Writer.TryWrite(new FunctionRpcMessageObservation(side, message));
            return ValueTask.FromResult(_getDisposition(message));
        }
    }

    private sealed record FunctionRpcMessageObservation(FunctionRpcRelaySide Side, StreamingMessage Message);
}