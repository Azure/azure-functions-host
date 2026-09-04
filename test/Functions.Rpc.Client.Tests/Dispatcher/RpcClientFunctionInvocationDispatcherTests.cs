// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientFunctionInvocationDispatcherTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private readonly List<WorkerChannel> _channels = [];
    private readonly Mock<IWorkerChannelRegistry> _registry = new();

    public RpcClientFunctionInvocationDispatcherTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => [.. _channels]);
        _registry.Setup(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancellationToken) => WaitForChannelAsync(cancellationToken));
        _registry.Setup(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task InitializeAsync_ConfiguresInitializedChannels()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();

        Task initialization = dispatcher.InitializeAsync([function]);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await initialization.WaitAsync(TestTimeout);
        Assert.True(worker.Channel.InvocationBuffersInitialization.IsCompletedSuccessfully);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());

        Assert.Equal(FunctionInvocationDispatcherState.Initialized, dispatcher.State);
        Assert.Equal(function.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
        Assert.True(worker.Channel.FunctionInputBuffers.ContainsKey(function.GetFunctionId()));
    }

    [Fact]
    public async Task InvokeAsync_CompletesSuccessfulInvocation()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);

        await InitializeDispatcherAsync(dispatcher, function, worker);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);

        ScriptInvocationResult result = await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeAsync_SurfacesFunctionLoadFailure()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);

        Task initialization = dispatcher.InitializeAsync([function]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId(), succeeded: false);
        await initialization.WaitAsync(TestTimeout);
        await dispatcher.InvokeAsync(invocation);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.Contains("function load failed", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesInvocationCancellation()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        using CancellationTokenSource cancellationSource = new();
        ScriptInvocationContext invocation = CreateInvocation(function, cancellationSource.Token);

        await InitializeDispatcherAsync(dispatcher, function, worker);
        cancellationSource.Cancel();
        await dispatcher.InvokeAsync(invocation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_WaitsForFirstInitializedChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);

        await dispatcher.InitializeAsync([function]);
        Task invoke = dispatcher.InvokeAsync(invocation);
        Assert.False(invoke.IsCompleted);

        _channels.Add(worker.Channel);
        Task setup = dispatcher.SetupChannelAsync(worker.Channel);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        Assert.True(setup.IsCompletedSuccessfully);
        await setup.WaitAsync(TestTimeout);
        await invoke.WaitAsync(TestTimeout);

        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_CancellationWhileWaitingIsCallerVisible()
    {
        using CancellationTokenSource cancellationSource = new();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function, cancellationSource.Token);
        await dispatcher.InitializeAsync([function]);

        Task invoke = dispatcher.InvokeAsync(invocation);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoke.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_NoInitializedChannelTimesOut()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(50));
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);
        await dispatcher.InitializeAsync([function]);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.InvokeAsync(invocation).WaitAsync(TestTimeout));

        Assert.Contains("No client-backed worker channel became ready", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_SelectsReadyChannelsRoundRobin()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(second.Channel);
        _channels.Add(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, first, second);

        await dispatcher.InvokeAsync(CreateInvocation(function));
        await dispatcher.InvokeAsync(CreateInvocation(function));

        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_ReadyChannelUsesSingleRegistrySnapshot()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, worker);
        _registry.Invocations.Clear();

        await dispatcher.InvokeAsync(CreateInvocation(function));

        _registry.Verify(registry => registry.GetInitializedChannels(), Times.Once);
        _registry.Verify(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetupChannelAsync_CompletesWhenInvocationBuffersAreInitialized()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, first);

        _channels.Add(second.Channel);
        Task setup = dispatcher.SetupChannelAsync(second.Channel);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await setup.WaitAsync(TestTimeout);
        Assert.True(second.Channel.InvocationBuffersInitialization.IsCompletedSuccessfully);

        await second.SendFunctionLoadResponseAsync(function.GetFunctionId());
    }

    [Fact]
    public async Task InvokeAsync_ChannelFailureDoesNotPreventLaterDispatch()
    {
        await using ClientWorkerChannelTestHarness failed = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness healthy = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(failed.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext failedInvocation = CreateInvocation(function);
        await InitializeDispatcherAsync(dispatcher, function, failed);
        await dispatcher.InvokeAsync(failedInvocation);
        await failed.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        failed.Transport.CompleteResponses(new InvalidOperationException("transport failed"));
        await Assert.ThrowsAnyAsync<Exception>(() => failedInvocation.ResultSource.Task.WaitAsync(TestTimeout));
        _channels.Clear();
        _channels.Add(healthy.Channel);
        Task setup = dispatcher.SetupChannelAsync(healthy.Channel);
        await healthy.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await healthy.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await setup.WaitAsync(TestTimeout);

        await dispatcher.InvokeAsync(CreateInvocation(function));

        await healthy.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task GetWorkerStatusesAsync_NoInitializedChannelsReturnsEmpty()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();

        IDictionary<string, WorkerStatus> statuses = await dispatcher.GetWorkerStatusesAsync();

        Assert.Empty(statuses);
    }

    [Fact]
    public void Factory_ReturnsRegisteredDispatcher()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        RpcClientFunctionInvocationDispatcherFactory factory = new(dispatcher);

        IFunctionInvocationDispatcher first = factory.GetFunctionDispatcher();
        IFunctionInvocationDispatcher second = factory.GetFunctionDispatcher();

        Assert.Same(first, second);
        Assert.Same(dispatcher, first);
    }

    [Fact]
    public async Task Dispose_AllowsBestEffortDispatchWithoutDisposingChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, worker);

        dispatcher.Dispose();

        await dispatcher.InvokeAsync(CreateInvocation(function));

        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        Assert.Equal(FunctionInvocationDispatcherState.Disposed, dispatcher.State);
        Assert.Equal(0, worker.Transport.DisposeCount);
    }

    [Fact]
    public async Task PreShutdown_RejectsNewInvocations()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);

        dispatcher.PreShutdown();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(CreateInvocation(function)));
        Assert.Contains("stopping", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FunctionInvocationDispatcherState.Disposing, dispatcher.State);
    }

    private RpcClientFunctionInvocationDispatcher CreateDispatcher(TimeSpan? channelWaitTimeout = null)
        => new(
            _registry.Object,
            Options.Create(new ScriptJobHostOptions()),
            Options.Create(new ManagedDependencyOptions()),
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance,
            channelWaitTimeout ?? TimeSpan.FromSeconds(10));

    private async Task<WorkerChannel> WaitForChannelAsync(CancellationToken cancellationToken)
    {
        while (_channels.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        return _channels[0];
    }

    private static async Task InitializeDispatcherAsync(
        RpcClientFunctionInvocationDispatcher dispatcher,
        FunctionMetadata function,
        params ClientWorkerChannelTestHarness[] workers)
    {
        Task initialization = dispatcher.InitializeAsync([function]);
        foreach (ClientWorkerChannelTestHarness worker in workers)
        {
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());
        }

        await initialization.WaitAsync(TestTimeout);
    }

    private static FunctionMetadata CreateFunction()
    {
        FunctionMetadata function = new()
        {
            Language = "external",
            Name = "TestFunction",
        };
        return function;
    }

    private static ScriptInvocationContext CreateInvocation(FunctionMetadata function, CancellationToken cancellationToken = default)
        => new()
        {
            FunctionMetadata = function,
            ExecutionContext = new()
            {
                FunctionName = function.Name,
                InvocationId = Guid.NewGuid(),
            },
            BindingData = [],
            Inputs = [],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            CancellationToken = cancellationToken,
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = NullLogger.Instance,
        };
}
