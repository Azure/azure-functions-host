// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task IdleRetirement_NegativeThresholdIsLoggedAndSkipped()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        await ReadyIdleWorkerAsync(registry, worker, CreateFunction("Function"));
        time.Advance(TimeSpan.FromMinutes(6));

        await registry.RetireIdleHttpWorkersAsync(TimeSpan.FromTicks(-1));

        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Same(worker.Channel, Assert.Single(await registry.GetReadyChannelsAsync()));
        Assert.Equal(0, worker.DisposeCount);
        harness.RegistryLogger.Verify(logger => logger.Log(
            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);

        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_DisposesWithoutWaitingForPeerCompletion()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        await ReadyIdleWorkerAsync(registry, worker, CreateFunction("Function"));
        time.Advance(TimeSpan.FromMinutes(6));

        Task retirement = registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.True(worker.DisposeStarted.IsCompleted);
        await retirement.WaitAsync(TestTimeout);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.False(worker.Channel.IsReadyForInvocations);
        Assert.Equal(1, worker.DisposeCount);
        harness.RegistryLogger.Verify(logger => logger.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
    }

    [Fact]
    public async Task IdleRetirement_PublishesZeroCapacityBeforeTransportDisposalCompletes()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = new("worker", blockDisposal: true)
        {
            Capabilities = new Dictionary<string, string>
            {
                ["FunctionGroupName"] = "http",
                ["IsAlwaysReady"] = "False",
                ["MaxHttpConcurrency"] = "16",
            },
        };
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        using RuntimeStateManager manager = CreateRuntimeStateManager(registry);
        await manager.StartAsync(CancellationToken.None);
        await ReadyIdleWorkerAsync(registry, worker, CreateFunction("Function"));
        await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 1 && state.HttpCapacity == 16);
        time.Advance(TimeSpan.FromMinutes(6));
        Task retirement = registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        try
        {
            await worker.DisposeStarted.WaitAsync(TestTimeout);
            await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 0 && state.HttpCapacity == 0);
            Assert.False(retirement.IsCompleted);
        }
        finally
        {
            worker.AllowDispose();
            await retirement.WaitAsync(TestTimeout);
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task IdleMonitor_ScansEachMinute_StrictThreshold_RemovesLastWorker_StopsWithRoot()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("http");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("HttpFunction");
        await ReadyIdleWorkerAsync(registry, worker, function);
        TaskCompletionSource timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<TimeProvider> monitorClock = new();
        Mock<ITimer> timer = new();
        monitorClock.Setup(clock => clock.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period) =>
            {
                ITimer scheduled = time.CreateTimer(callback, state, dueTime, period);
                timer.Setup(value => value.Dispose()).Callback(scheduled.Dispose);
                timerCreated.SetResult();
                return timer.Object;
            });
        using HttpWorkerIdleMonitor monitor = new(registry, monitorClock.Object);
        await monitor.StartAsync(CancellationToken.None);
        await timerCreated.Task.WaitAsync(TestTimeout);

        for (int minute = 1; minute <= 5; minute++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
            Assert.Single(registry.GetInitializedChannels());
            Assert.Equal(0, worker.DisposeCount);
        }

        time.Advance(TimeSpan.FromMinutes(1));
        await worker.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);
        Assert.False(worker.Channel.IsReadyForInvocations);
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(1, worker.DisposeCount);
        WorkerLinkException replay = await Assert.ThrowsAsync<WorkerLinkException>(() =>
            registry.LinkAsync(worker.Id, CreateEndpoint(worker.Id)));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, replay.Reason);

        await monitor.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        timer.Verify(value => value.Dispose(), Times.Once);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_MixedWorkers_ExemptsAlwaysReadyUnknownNonHttpAndBusyWorkers()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl idle = CreateIdleWorker("idle");
        ChannelControl secondIdle = CreateIdleWorker("second-idle");
        ChannelControl alwaysReady = CreateIdleWorker("always-ready", alwaysReady: "True");
        ChannelControl unknown = CreateIdleWorker("unknown", alwaysReady: null);
        ChannelControl invalid = CreateIdleWorker("invalid", alwaysReady: "not-a-bool");
        ChannelControl nonHttp = CreateIdleWorker("timer", group: "function:timer");
        ChannelControl loading = CreateIdleWorker("loading");
        ChannelControl busy = CreateIdleWorker("busy");
        ChannelControl[] workers = [idle, secondIdle, alwaysReady, unknown, invalid, nonHttp, loading, busy];
        foreach (ChannelControl worker in workers)
        {
            harness.Enqueue(worker);
        }

        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null);
        foreach (ChannelControl worker in workers)
        {
            await LinkAsync(registry, worker.Id);
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            if (!ReferenceEquals(worker, loading))
            {
                CompleteFunctionLoad(worker, function);
            }
        }

        ScriptInvocationContext invocation = CreateIdleInvocation(function);
        using RpcClientFunctionInvocationDispatcher dispatcher = await CreateIdleDispatcherAsync(busy, function);
        await dispatcher.InvokeAsync(invocation);
        time.Advance(TimeSpan.FromMinutes(6));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);

        Assert.Equal(1, idle.DisposeCount);
        Assert.Equal(1, secondIdle.DisposeCount);
        Assert.Equal(1, loading.DisposeCount);
        Assert.Equal(5, registry.GetInitializedChannels().Count);
        Assert.All(workers.Where(worker => !ReferenceEquals(worker, idle) && !ReferenceEquals(worker, secondIdle) && !ReferenceEquals(worker, loading)),
            worker => Assert.Equal(0, worker.DisposeCount));

        time.Advance(TimeSpan.FromMinutes(10));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(5, registry.GetInitializedChannels().Count);
        invocation.ResultSource.TrySetResult(new());
        await WaitUntilAsync(() => busy.Channel.OngoingRequests == 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task IdleRetirement_InitialGraceDoesNotDependOnFunctionLoadResponses(int functionCount)
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata[] functions = [.. Enumerable.Range(0, functionCount).Select(index => CreateFunction($"Function{index}"))];
        await registry.ActivateAsync(functions, new ManagedDependencyOptions(), null);
        await LinkAsync(registry, worker.Id);
        await registry.GetReadyChannelsAsync().AsTask().WaitAsync(TestTimeout);

        foreach (FunctionMetadata function in functions)
        {
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        }

        time.Advance(IdleThreshold);
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(0, worker.DisposeCount);
        time.Advance(TimeSpan.FromMinutes(2));
        if (functionCount > 0)
        {
            CompleteFunctionLoad(worker, functions[0]);
        }

        time.Advance(TimeSpan.FromMinutes(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(0, worker.DisposeCount);
        time.Advance(TimeSpan.FromTicks(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task IdleRetirement_ReactivationRestartsInitialGrace(int activationCount)
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, worker.Id);
        FunctionMetadata function = CreateFunction("Function");
        for (int i = 0; i < activationCount; i++)
        {
            await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            time.Advance(TimeSpan.FromMinutes(2));
            await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
            Assert.Equal(0, worker.DisposeCount);
            Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
        }

        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(0, worker.DisposeCount);
        time.Advance(TimeSpan.FromTicks(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_FirstRequestSwitchesToTwoMinuteIdleThreshold()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await ReadyIdleWorkerAsync(registry, worker, function);
        time.Advance(TimeSpan.FromMinutes(1));
        using RpcClientFunctionInvocationDispatcher dispatcher = await CreateIdleDispatcherAsync(worker, function);
        await dispatcher.InvokeAsync(CreateIdleInvocation(function));
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.Channel.InvokeResponse(new()
        {
            InvocationId = request.InvocationRequest.InvocationId,
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);

        time.Advance(IdleThreshold);
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(0, worker.DisposeCount);
        time.Advance(TimeSpan.FromTicks(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_QueuedAndOverlappingRequests_ResetContinuousIdleTime()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        using RpcClientFunctionInvocationDispatcher dispatcher = new(registry, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext first = CreateIdleInvocation(function);
        ScriptInvocationContext second = CreateIdleInvocation(function);

        await dispatcher.InvokeAsync(first);
        await dispatcher.InvokeAsync(second);
        Assert.Equal(2, worker.Channel.OngoingRequests);
        Assert.False(worker.Channel.IsExecutingInvocation(first.ExecutionContext.InvocationId.ToString()));
        Assert.False(worker.Channel.IsExecutingInvocation(second.ExecutionContext.InvocationId.ToString()));
        time.Advance(TimeSpan.FromMinutes(5));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(0, worker.DisposeCount);
        Assert.False(worker.Transport.Requests.TryRead(out _));

        CompleteFunctionLoad(worker, function);
        StreamingMessage request1 = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        StreamingMessage request2 = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.Channel.InvokeResponse(new() { InvocationId = request1.InvocationRequest.InvocationId, Result = new() { Status = StatusResult.Types.Status.Success } });
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 1);
        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(0, worker.DisposeCount);
        await worker.Channel.InvokeResponse(new() { InvocationId = request2.InvocationRequest.InvocationId, Result = new() { Status = StatusResult.Types.Status.Success } });
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);
        time.Advance(TimeSpan.FromSeconds(119));

        ScriptInvocationContext brief = CreateIdleInvocation(function);
        await dispatcher.InvokeAsync(brief);
        StreamingMessage request3 = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.Channel.InvokeResponse(new() { InvocationId = request3.InvocationRequest.InvocationId, Result = new() { Status = StatusResult.Types.Status.Success } });
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);
        time.Advance(TimeSpan.FromSeconds(120));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(0, worker.DisposeCount);
        time.Advance(TimeSpan.FromSeconds(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IdleRetirement_WaitsForStreamingBodyAndResponseCompletion_EvenOnFailureOrCancellation(bool failure, bool canceled)
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        TaskCompletionSource<ForwarderError> body = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> proxy = new();
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), It.IsAny<Uri>()))
            .Callback((ScriptInvocationContext context, Uri _) => context.Properties[ScriptConstants.HttpProxyTask] = body.Task);
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(async () => { await body.Task; });
        ChannelControl worker = new("streaming")
        {
            Capabilities = new Dictionary<string, string>
            {
                ["FunctionGroupName"] = "http",
                ["IsAlwaysReady"] = "False",
                ["MaxHttpConcurrency"] = "16",
                ["HttpUri"] = "http://worker:8080/"
            },
            HttpProxyService = proxy.Object,
        };
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Stream");
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        await ReadyIdleWorkerAsync(registry, worker, function);
        ScriptInvocationContext invocation = CreateIdleInvocation(function);
        DefaultHttpContext httpContext = new();
        httpContext.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = null;
        Mock<IHttpResponseFeature> response = new();
        response.SetupGet(feature => feature.Headers).Returns(new HeaderDictionary());
        response.SetupGet(feature => feature.HasStarted).Returns(true);
        Func<object, Task> completeResponse = null;
        object completionState = null;
        response.Setup(feature => feature.OnCompleted(It.IsAny<Func<object, Task>>(), It.IsAny<object>()))
            .Callback((Func<object, Task> callback, object state) =>
            {
                completeResponse = callback;
                completionState = state;
            });
        httpContext.Features.Set(response.Object);
        invocation.Inputs = [("req", DataType.String, httpContext.Request)];
        using CancellationTokenSource cancellation = new();
        invocation.CancellationToken = cancellation.Token;
        using RpcClientFunctionInvocationDispatcher dispatcher = await CreateIdleDispatcherAsync(worker, function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        cancellation.Cancel();
        if (canceled)
        {
            invocation.ResultSource.TrySetCanceled();
        }

        InvocationResponse rpcResponse = new()
        {
            InvocationId = request.InvocationRequest.InvocationId,
            Result = new() { Status = StatusResult.Types.Status.Success },
        };
        Task responseProcessing = canceled ? Task.CompletedTask : worker.Channel.InvokeResponse(rpcResponse);
        if (!canceled)
        {
            Assert.False(responseProcessing.IsCompleted);
            await worker.Channel.InvokeResponse(rpcResponse);
            proxy.Verify(service => service.EnsureSuccessfulForwardingAsync(invocation), Times.Once);
        }

        time.Advance(TimeSpan.FromMinutes(4));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(1, worker.Channel.OngoingRequests);
        Assert.Equal(0, worker.DisposeCount);

        if (failure)
        {
            body.SetException(new InvalidOperationException("forwarding failed"));
        }
        else
        {
            body.SetResult(ForwarderError.None);
        }

        await responseProcessing.WaitAsync(TestTimeout);

        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(1, worker.Channel.OngoingRequests);
        Assert.Equal(0, worker.DisposeCount);
        await completeResponse(completionState);
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);
        time.Advance(TimeSpan.FromSeconds(120));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(0, worker.DisposeCount);
        time.Advance(TimeSpan.FromSeconds(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleRetirement_StandaloneHttpContextDoesNotWaitForResponseCallback(bool failed)
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("standalone");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("HttpFunction");
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        await ReadyIdleWorkerAsync(registry, worker, function);
        ScriptInvocationContext invocation = CreateIdleInvocation(function);
        invocation.Inputs = [("req", DataType.String, new DefaultHttpContext().Request)];

        using RpcClientFunctionInvocationDispatcher dispatcher = await CreateIdleDispatcherAsync(worker, function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.Channel.InvokeResponse(new()
        {
            InvocationId = request.InvocationRequest.InvocationId,
            Result = new() { Status = failed ? StatusResult.Types.Status.Failure : StatusResult.Types.Status.Success },
        });
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);
        Assert.Equal(failed, invocation.ResultSource.Task.IsFaulted);

        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_RejectedQueueDoesNotLeaveTrackedInvocation()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await ReadyIdleWorkerAsync(registry, worker, function);
        worker.Channel.FunctionInputBuffers[function.GetFunctionId()].Complete();
        ScriptInvocationContext invocation = CreateIdleInvocation(function);
        DefaultHttpContext httpContext = new();
        httpContext.Items[ScriptConstants.AzureFunctionsHttpTriggerContext] = null;
        Func<object, Task> completeResponse = null;
        object completionState = null;
        Mock<IHttpResponseFeature> response = new();
        response.Setup(feature => feature.OnCompleted(It.IsAny<Func<object, Task>>(), It.IsAny<object>()))
            .Callback((Func<object, Task> callback, object state) =>
            {
                completeResponse = callback;
                completionState = state;
            });
        httpContext.Features.Set(response.Object);
        invocation.Inputs = [("req", DataType.String, httpContext.Request)];

        using RpcClientFunctionInvocationDispatcher dispatcher = await CreateIdleDispatcherAsync(worker, function);
        await dispatcher.InvokeAsync(invocation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.ResultSource.Task);
        await completeResponse(completionState);

        Assert.Equal(0, worker.Channel.OngoingRequests);
        Assert.False(worker.Channel.IsExecutingInvocation(invocation.ExecutionContext.InvocationId.ToString()));
        time.Advance(TimeSpan.FromMinutes(6));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_AdmissionRace_EitherRetiresOrProtectsAcceptedInvocation()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await ReadyIdleWorkerAsync(registry, worker, function);
        time.Advance(TimeSpan.FromMinutes(6));
        ScriptInvocationContext invocation = CreateIdleInvocation(function);
        using RpcClientFunctionInvocationDispatcher dispatcher = await CreateIdleDispatcherAsync(worker, function);
        using ManualResetEventSlim start = new();
        Task<bool> dispatch = Task.Run(async () =>
        {
            start.Wait();
            try
            {
                await dispatcher.InvokeAsync(invocation);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        });
        Task retirement = Task.Run(async () =>
        {
            start.Wait();
            await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        });
        start.Set();
        bool accepted = await dispatch.WaitAsync(TestTimeout);
        await retirement.WaitAsync(TestTimeout);

        if (accepted)
        {
            Assert.Equal(0, worker.DisposeCount);
            Assert.Single(registry.GetInitializedChannels());
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            invocation.ResultSource.TrySetResult(new());
            await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);
        }
        else
        {
            Assert.Equal(1, worker.DisposeCount);
            Assert.Empty(registry.GetInitializedChannels());
        }
    }

    [Fact]
    public async Task IdleRetirement_WithdrawsBeforeDisposal_FencesStaleDispatcherSnapshot_ConcurrentUnlinkIsIdempotent()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = new("retiring", blockDisposal: true)
        {
            Capabilities = new Dictionary<string, string> { ["FunctionGroupName"] = "http", ["IsAlwaysReady"] = "False", ["MaxHttpConcurrency"] = "16" },
        };
        ChannelControl replacement = CreateIdleWorker("always-ready", alwaysReady: "True");
        harness.Enqueue(worker);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await ReadyIdleWorkerAsync(registry, worker, function);
        await LinkAsync(registry, replacement.Id);
        await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        CompleteFunctionLoad(replacement, function);
        time.Advance(TimeSpan.FromMinutes(6));
        Task retirement = registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);
        await worker.DisposeStarted.WaitAsync(TestTimeout);
        Task<bool> unlink = registry.UnlinkAsync(worker.Id);
        try
        {
            Mock<IWorkerChannelRegistry> staleRegistry = new();
            staleRegistry.SetupSequence(value => value.GetReadyChannelsAsync())
                .Returns(new ValueTask<IReadOnlyList<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel>>([worker.Channel]))
                .Returns(() => registry.GetReadyChannelsAsync());
            using RpcClientFunctionInvocationDispatcher dispatcher = new(staleRegistry.Object, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
            await dispatcher.InitializeAsync([function]);
            await dispatcher.InvokeAsync(CreateIdleInvocation(function));
            await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            Assert.Same(replacement.Channel, Assert.Single(registry.GetInitializedChannels()));
            Assert.Equal(0, worker.Channel.OngoingRequests);
        }
        finally
        {
            worker.AllowDispose();
            await retirement.WaitAsync(TestTimeout);
            Assert.False(await unlink.WaitAsync(TestTimeout));
        }

        Assert.Equal(1, worker.DisposeCount);
    }

    [Fact]
    public async Task IdleRetirement_DisposalFailureIsLogged_AndRemainingWorkersAreStillRetired()
    {
        FakeTimeProvider time = new();
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl failed = CreateIdleWorker("failed");
        ChannelControl healthy = CreateIdleWorker("healthy");
        IOException failure = new("teardown failed");
        failed.Transport.DisposeException = failure;
        harness.Enqueue(failed);
        harness.Enqueue(healthy);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null);
        foreach (ChannelControl worker in new[] { failed, healthy })
        {
            await LinkAsync(registry, worker.Id);
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            CompleteFunctionLoad(worker, function);
        }

        time.Advance(TimeSpan.FromMinutes(6));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold, CancellationToken.None);

        Assert.Empty(registry.GetInitializedChannels());
        Assert.Equal(1, failed.DisposeCount);
        Assert.Equal(1, healthy.DisposeCount);
        harness.RegistryLogger.Verify(logger => logger.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), failure,
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    private static async Task<RpcClientFunctionInvocationDispatcher> CreateIdleDispatcherAsync(ChannelControl worker, FunctionMetadata function)
    {
        Mock<IWorkerChannelRegistry> registry = new();
        registry.Setup(value => value.GetReadyChannelsAsync())
            .Returns(new ValueTask<IReadOnlyList<Microsoft.Azure.WebJobs.Script.Grpc.WorkerChannel>>([worker.Channel]));
        RpcClientFunctionInvocationDispatcher dispatcher = new(
            registry.Object, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance, TimeSpan.FromMilliseconds(100));
        await dispatcher.InitializeAsync([function]);
        return dispatcher;
    }

    private static ChannelControl CreateIdleWorker(string id, string group = "http", string alwaysReady = "False")
    {
        Dictionary<string, string> capabilities = new() { ["FunctionGroupName"] = group, ["MaxHttpConcurrency"] = "16" };
        if (alwaysReady is not null)
        {
            capabilities["IsAlwaysReady"] = alwaysReady;
        }

        return new(id) { Capabilities = capabilities };
    }

    private static async Task ReadyIdleWorkerAsync(WorkerChannelRegistry registry, ChannelControl worker, FunctionMetadata function)
    {
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        CompleteFunctionLoad(worker, function);
    }

    private static ScriptInvocationContext CreateIdleInvocation(FunctionMetadata function)
        => new()
        {
            FunctionMetadata = function,
            ExecutionContext = new() { InvocationId = Guid.NewGuid(), FunctionName = function.Name },
            Inputs = [],
            BindingData = [],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = NullLogger.Instance,
        };

    private static void CompleteFunctionLoad(ChannelControl worker, FunctionMetadata function)
        => worker.Channel.LoadResponse(new FunctionLoadResponse
        {
            FunctionId = function.GetFunctionId(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
}
