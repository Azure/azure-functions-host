// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task Relay_TracksContextAndForwardsMessagesUnchanged()
    {
        CapturingInterceptorFactory interceptorFactory = new();
        await using WorkerProxyWebApplicationFactory factory = CreateContextFactory(interceptorFactory);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage functionLoad = CreateFunctionLoadMessage("function-1", "Function One");
        StreamingMessage invocation = CreateInvocationMessage("invocation-1", "function-1");

        await runtime.WriteAsync(functionLoad, timeout.Token);
        await runtime.WriteAsync(invocation, timeout.Token);

        Assert.Equal(functionLoad, await worker.ReadAsync(timeout.Token));
        Assert.Equal(invocation, await worker.ReadAsync(timeout.Token));
        FunctionRpcContextTrackingInterceptor interceptor = await interceptorFactory.GetAsync(0, timeout.Token);
        Assert.True(interceptor.TryGetInvocation("invocation-1", out FunctionRpcInvocationContext? context));
        Assert.Equal("Function One", context?.FunctionName);

        StreamingMessage response = new()
        {
            InvocationResponse = new() { InvocationId = "invocation-1" }
        };
        await worker.WriteAsync(response, timeout.Token);

        Assert.Equal(response, await runtime.ReadAsync(timeout.Token));
        Assert.False(interceptor.TryGetInvocation("invocation-1", out _));
        Assert.Single(interceptorFactory.Interceptors);
    }

    [Fact]
    public async Task Relay_ReplacementSessionReceivesNewEmptyContext()
    {
        CapturingInterceptorFactory interceptorFactory = new();
        await using WorkerProxyWebApplicationFactory factory = CreateContextFactory(interceptorFactory);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);

        await using (RelayClient firstRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token))
        await using (RelayClient firstWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token))
        {
            StreamingMessage functionLoad = CreateFunctionLoadMessage("function-1", "Function One");
            StreamingMessage invocation = CreateInvocationMessage("invocation-1", "function-1");
            StreamingMessage startStream = new() { StartStream = new() { WorkerId = "worker-1" } };
            await firstRuntime.WriteAsync(functionLoad, timeout.Token);
            await firstRuntime.WriteAsync(invocation, timeout.Token);
            await firstWorker.WriteAsync(startStream, timeout.Token);
            await firstWorker.ReadAsync(timeout.Token);
            await firstWorker.ReadAsync(timeout.Token);
            await firstRuntime.ReadAsync(timeout.Token);

            FunctionRpcContextTrackingInterceptor first = await interceptorFactory.GetAsync(0, timeout.Token);
            Assert.Equal("worker-1", first.Worker.WorkerId);
            Assert.True(first.TryGetFunction("function-1", out _));
            Assert.True(first.TryGetInvocation("invocation-1", out _));

            await firstRuntime.CompleteRequestAsync(timeout.Token);
            await Task.WhenAll(
                firstRuntime.WaitForTerminationAsync(timeout.Token),
                firstWorker.WaitForTerminationAsync(timeout.Token));
        }

        await WaitForReleaseAsync(relay, timeout.Token);
        await using RelayClient secondRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient secondWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(secondRuntime, secondWorker, "replacement", timeout.Token);

        FunctionRpcContextTrackingInterceptor replacement = await interceptorFactory.GetAsync(1, timeout.Token);
        Assert.NotSame(interceptorFactory.Interceptors[0], replacement);
        Assert.Equal(new FunctionRpcWorkerContext(null, null), replacement.Worker);
        Assert.False(replacement.TryGetFunction("function-1", out _));
        Assert.False(replacement.TryGetInvocation("invocation-1", out _));
        Assert.Equal(2, interceptorFactory.Interceptors.Count);
    }

    [Fact]
    public async Task Relay_LogsBeforeInvocationResponseResolveActiveInvocationInOrder()
    {
        OrderingInterceptorFactory interceptorFactory = new();
        await using WorkerProxyWebApplicationFactory factory = CreateContextFactory(interceptorFactory);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        StreamingMessage invocation = CreateInvocationMessage("invocation-1", "function-1");
        await runtime.WriteAsync(invocation, timeout.Token);
        Assert.Equal(invocation, await worker.ReadAsync(timeout.Token));
        OrderingInterceptor interceptor = await interceptorFactory.GetAsync(timeout.Token);

        StreamingMessage firstLog = CreateMessage("log-1");
        firstLog.RpcLog.InvocationId = "invocation-1";
        StreamingMessage secondLog = CreateMessage("log-2");
        secondLog.RpcLog.InvocationId = "invocation-1";
        StreamingMessage response = new() { InvocationResponse = new() { InvocationId = "invocation-1" } };
        await worker.WriteAllAsync([firstLog, secondLog, response], timeout.Token);

        Assert.Equal(firstLog, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(secondLog, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(response, await runtime.ReadAsync(timeout.Token));
        Assert.Equal([true, true], interceptor.LogLookups);
        Assert.False(interceptor.Context.TryGetInvocation("invocation-1", out _));
    }

    private static WorkerProxyWebApplicationFactory CreateContextFactory(IFunctionRpcMessageInterceptorFactory interceptorFactory)
    {
        return new WorkerProxyWebApplicationFactory(
            configureServices: services => services.AddSingleton(interceptorFactory));
    }

    private static StreamingMessage CreateFunctionLoadMessage(string functionId, string functionName)
    {
        return new StreamingMessage
        {
            FunctionLoadRequest = new()
            {
                FunctionId = functionId,
                Metadata = new() { Name = functionName }
            }
        };
    }

    private static StreamingMessage CreateInvocationMessage(string invocationId, string functionId)
    {
        return new StreamingMessage
        {
            InvocationRequest = new() { InvocationId = invocationId, FunctionId = functionId }
        };
    }

    private sealed class CapturingInterceptorFactory : IFunctionRpcMessageInterceptorFactory
    {
        private readonly Lock _syncLock = new();
        private readonly List<FunctionRpcContextTrackingInterceptor> _interceptors = [];

        public IReadOnlyList<FunctionRpcContextTrackingInterceptor> Interceptors
        {
            get
            {
                lock (_syncLock)
                {
                    return _interceptors.ToArray();
                }
            }
        }

        public IFunctionRpcMessageInterceptor Create()
        {
            FunctionRpcContextTrackingInterceptor interceptor = new();
            lock (_syncLock)
            {
                _interceptors.Add(interceptor);
            }

            return interceptor;
        }

        public async Task<FunctionRpcContextTrackingInterceptor> GetAsync(int index, CancellationToken cancellationToken)
        {
            while (Interceptors.Count <= index)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }

            return Interceptors[index];
        }
    }

    private sealed class OrderingInterceptorFactory : IFunctionRpcMessageInterceptorFactory
    {
        private readonly TaskCompletionSource<OrderingInterceptor> _created =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IFunctionRpcMessageInterceptor Create()
        {
            OrderingInterceptor interceptor = new();
            _created.SetResult(interceptor);
            return interceptor;
        }

        public Task<OrderingInterceptor> GetAsync(CancellationToken cancellationToken)
        {
            return _created.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class OrderingInterceptor : IFunctionRpcMessageInterceptor
    {
        private readonly List<bool> _logLookups = [];

        public FunctionRpcContextTrackingInterceptor Context { get; } = new();

        public IReadOnlyList<bool> LogLookups => _logLookups;

        public async ValueTask<FunctionRpcMessageDisposition> ProcessAsync(
            FunctionRpcRelaySide side,
            StreamingMessage message,
            CancellationToken cancellationToken)
        {
            if (side is FunctionRpcRelaySide.Worker && message.RpcLog is not null)
            {
                _logLookups.Add(Context.TryGetInvocation(message.RpcLog.InvocationId, out _));
            }

            return await Context.ProcessAsync(side, message, cancellationToken);
        }
    }
}