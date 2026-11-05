// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Fact]
    public async Task RuntimeDrain_RejectsLinkThatStartedBeforeAdmissionClosed()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        ChannelControl pending = new("pending", blockStart: true);
        harness.Enqueue(pending);
        Task<WorkerLinkResult> link = registry.LinkAsync(
            pending.Id,
            CreateEndpoint(pending.Id));
        await pending.StartEntered.WaitAsync(TestTimeout);
        IRuntimeDrainManager manager = new RuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<RuntimeDrainManager>.Instance);

        manager.Accept(RuntimeDrainModel.Immediate);
        pending.AllowStart();

        WorkerLinkException admissionClosed =
            await Assert.ThrowsAsync<WorkerLinkException>(
                () => link.WaitAsync(TestTimeout));
        Assert.Equal(
            WorkerLinkFailureReason.RuntimeStopping,
            admissionClosed.Reason);
        await WaitUntilAsync(() => pending.DisposeCount == 1);
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task RuntimeDrain_EscalationWithdrawsHttpCapacityWhileAcceptedWorkCompletes()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("Function");
        ChannelControl background = CreateClassifiedWorker("background");
        ChannelControl firstHttp = new(
            "http-first", functionGroupName: FunctionGroups.Http, isAlwaysReady: false, maxHttpConcurrency: 7);
        ChannelControl secondHttp = new(
            "http-second", functionGroupName: "HTTP", isAlwaysReady: true, maxHttpConcurrency: 10);
        ChannelControl[] httpWorkers = [firstHttp, secondHttp];
        await LinkAndReadyWorkersAsync(harness, registry, function, background, firstHttp, secondHttp);
        using RuntimeStateManager stateManager = CreateRuntimeStateManager(registry);
        await stateManager.StartAsync(CancellationToken.None);
        await WaitForRuntimeStateAsync(stateManager, state => state.WorkerCount == 3 && state.HttpCapacity == 17);
        ScriptInvocationContext[] invocations =
        [
            CreateScaleInInvocation(function),
            CreateScaleInInvocation(function),
        ];
        StreamingMessage[] requests = new StreamingMessage[httpWorkers.Length];
        for (int index = 0; index < httpWorkers.Length; index++)
        {
            Assert.True(httpWorkers[index].Channel.TryDispatchInvocation(invocations[index]));
            requests[index] = await httpWorkers[index].ReadRequestAsync(
                StreamingMessage.ContentOneofCase.InvocationRequest);
        }

        IRuntimeDrainManager manager = new RuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<RuntimeDrainManager>.Instance);
        RuntimeDrainReceipt phased = manager.Accept(RuntimeDrainModel.Phased);
        await background.DisposeStarted.WaitAsync(TestTimeout);
        await WaitForRuntimeStateAsync(stateManager, state => state.WorkerCount == 2 && state.HttpCapacity == 17);
        Assert.All(httpWorkers, worker => Assert.True(worker.Channel.IsReadyForInvocations));
        Task changed = registry.GetChangeTask();

        RuntimeDrainReceipt immediate = manager.Accept(RuntimeDrainModel.Immediate);

        Assert.Equal(phased.AcceptedAtUtc, immediate.AcceptedAtUtc);
        await changed.WaitAsync(TestTimeout);
        await WaitForRuntimeStateAsync(stateManager, state => state.WorkerCount == 2 && state.HttpCapacity == 0);
        Assert.All(httpWorkers, worker =>
        {
            Assert.False(worker.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
            Assert.False(worker.DisposeStarted.IsCompleted);
        });
        Assert.All(invocations, invocation => Assert.False(invocation.ResultSource.Task.IsCompleted));
        for (int index = 0; index < httpWorkers.Length; index++)
        {
            ChannelControl worker = httpWorkers[index];
            await worker.Transport.SendResponseAsync(new()
            {
                InvocationResponse = new()
                {
                    InvocationId = requests[index].InvocationRequest.InvocationId,
                    Result = new() { Status = StatusResult.Types.Status.Success },
                },
            });
            await invocations[index].ResultSource.Task.WaitAsync(TestTimeout);
            await worker.DisposeStarted.WaitAsync(TestTimeout);
            await WaitUntilAsync(() => worker.DisposeCount == 1);
        }

        await WaitForRuntimeStateAsync(stateManager, state => state.WorkerCount == 0 && state.HttpCapacity == 0);
        await stateManager.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeDrain_HttpScaleInReusesDrainAndUnlinkUntilCompletionOrDeadline(bool expireDeadline)
    {
        Mock<TimeProvider> clock = new() { CallBase = true };
        RegistryHarness harness = new(linkTimeout: TestTimeout, timeProvider: clock.Object);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateHttpFunction();
        ChannelControl selected = CreateHttpWorker("always-ready", true, 10);
        ChannelControl onDemand = CreateHttpWorker("on-demand", false, 17);
        int unlinkOutcomes = 0;
        TaskCompletionSource unlinked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Logger.Setup(logger => logger.Log(
            It.IsAny<LogLevel>(), It.Is<EventId>(eventId => eventId.Id == 9 || eventId.Id == 11),
            It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(new InvocationAction(call =>
            {
                var fields = (IEnumerable<KeyValuePair<string, object>>)call.Arguments[2];
                if (fields.Any(pair =>
                    string.Equals(pair.Key, "WorkerId", StringComparison.Ordinal) &&
                    pair.Value is string workerId && string.Equals(workerId, selected.Id, StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref unlinkOutcomes);
                    unlinked.TrySetResult();
                }
            }));
        await LinkAndReadyWorkersAsync(harness, registry, function, selected, onDemand);
        using Activity activity = new Activity("RuntimeDrainAfterHttpScaleIn").Start();
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryDispatchInvocation(invocation));
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await using CapacityAppServer appServer = new();
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry, appServer.Configure);
        await appServer.WaitForCapacityAsync(27);
        appServer.Publish = (state, _) =>
        {
            if (state.HttpCapacity == 17)
            {
                Assert.True(selected.Channel.IsReadyForInvocations);
                Assert.False(selected.DisposeStarted.IsCompleted);
            }

            return Task.FromResult(HttpStatusCode.OK);
        };
        using HttpResponseMessage scaleIn = await host.PostAsync(
            """{"functionGroup":"http","isAlwaysReady":true,"desiredWorkerCount":0}""");
        Assert.Equal(1, await ReadNewlyDrainingWorkerCountAsync(scaleIn));
        Task drain = selected.Channel.TryBeginRuntimeDrain();
        Assert.NotNull(drain);
        Assert.False(drain.IsCompleted);
        Assert.True(onDemand.Channel.IsReadyForInvocations);
        appServer.Publish = (_, _) => Task.FromResult(HttpStatusCode.OK);
        TimerCallback expire = null;
        object timerState = null;
        clock.Setup(provider => provider.GetTimestamp()).Returns(0);
        clock.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object>(), TimeSpan.FromMinutes(60), Timeout.InfiniteTimeSpan))
            .Returns((TimerCallback callback, object state, TimeSpan _, TimeSpan _) =>
            {
                expire = callback;
                timerState = state;
                return Mock.Of<ITimer>();
            });
        IRuntimeDrainManager manager = new RuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<RuntimeDrainManager>.Instance);

        manager.Accept(RuntimeDrainModel.Immediate);

        Assert.Same(drain, selected.Channel.TryBeginRuntimeDrain());
        Assert.NotNull(expire);
        Assert.False(selected.DisposeStarted.IsCompleted);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
        await onDemand.DisposeStarted.WaitAsync(TestTimeout);
        await appServer.WaitForCapacityAsync(0);
        if (expireDeadline)
        {
            expire(timerState);
            TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(
                () => invocation.ResultSource.Task.WaitAsync(TestTimeout));
            Assert.Contains(selected.Id, failure.Message, StringComparison.Ordinal);
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Single(activity.Events);
        }
        else
        {
            await CompleteHttpWorkAsync(selected, invocation);
        }

        await drain.WaitAsync(TestTimeout);
        await selected.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => selected.DisposeCount == 1 && onDemand.DisposeCount == 1);
        await unlinked.Task.WaitAsync(TestTimeout);
        Assert.Equal(1, Volatile.Read(ref unlinkOutcomes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeDrain_PendingHttpPublicationDrainsAllClassesWithoutReopeningAdmission(bool publicationFails)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateHttpFunction();
        ChannelControl alwaysReady = CreateHttpWorker("always-ready", true, 10);
        ChannelControl onDemand = CreateHttpWorker("on-demand", false, 17);
        await LinkAndReadyWorkersAsync(harness, registry, function, alwaysReady, onDemand);
        ScriptInvocationContext alwaysReadyWork = CreateScaleInInvocation(function);
        ScriptInvocationContext onDemandWork = CreateScaleInInvocation(function);
        Assert.True(alwaysReady.Channel.TryDispatchInvocation(alwaysReadyWork));
        Assert.True(onDemand.Channel.TryDispatchInvocation(onDemandWork));
        await alwaysReady.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await onDemand.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await using CapacityAppServer appServer = new();
        await using ScaleInApiHost host = await ScaleInApiHost.StartAsync(registry, appServer.Configure);
        await appServer.WaitForCapacityAsync(27);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource attempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        appServer.Publish = async (state, token) =>
        {
            if (state.HttpCapacity < 27)
            {
                attempted.TrySetResult();
                await release.Task.WaitAsync(token);
                return publicationFails ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
            }

            return HttpStatusCode.OK;
        };
        Task<HttpResponseMessage> scaleIn = host.PostAsync(
            """{"functionGroup":"http","isAlwaysReady":true,"desiredWorkerCount":0}""");
        try
        {
            await attempted.Task.WaitAsync(TestTimeout);
            Assert.True(alwaysReady.Channel.IsReadyForInvocations);
            Assert.True(onDemand.Channel.IsReadyForInvocations);
            IRuntimeDrainManager manager = new RuntimeDrainManager(
                registry,
                TimeProvider.System,
                NullLogger<RuntimeDrainManager>.Instance);

            manager.Accept(RuntimeDrainModel.Immediate);

            Assert.False(scaleIn.IsCompleted);
            Assert.False(alwaysReady.Channel.IsReadyForInvocations);
            Assert.False(onDemand.Channel.IsReadyForInvocations);
            Assert.False(alwaysReady.DisposeStarted.IsCompleted);
            Assert.False(onDemand.DisposeStarted.IsCompleted);
            WorkerLinkException admissionClosed = await Assert.ThrowsAsync<WorkerLinkException>(
                () => registry.LinkAsync("new-worker", CreateEndpoint("new-worker")));
            Assert.Equal(WorkerLinkFailureReason.RuntimeStopping, admissionClosed.Reason);
        }
        finally
        {
            release.TrySetResult();
        }

        using HttpResponseMessage response = await scaleIn.WaitAsync(TestTimeout);
        if (publicationFails)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        else
        {
            Assert.Equal(0, await ReadNewlyDrainingWorkerCountAsync(response));
        }

        appServer.Publish = (_, _) => Task.FromResult(HttpStatusCode.OK);
        await appServer.WaitForCapacityAsync(0);
        Assert.False(alwaysReady.Channel.IsHttpCapacityAvailable);
        Assert.False(onDemand.Channel.IsHttpCapacityAvailable);
        Assert.False(alwaysReady.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
        Assert.False(onDemand.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
        await CompleteHttpWorkAsync(alwaysReady, alwaysReadyWork);
        await CompleteHttpWorkAsync(onDemand, onDemandWork);
        await WaitUntilAsync(() => alwaysReady.DisposeCount == 1 && onDemand.DisposeCount == 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RuntimeDrain_SharedTrackingWaitsForAllAcceptedWorkAfterFailureOrCancellation(
        bool cancelFirst,
        bool scaleInFirst)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("selected");
        harness.Enqueue(selected);
        await LinkAsync(registry, selected.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await WaitUntilAsync(() => selected.Channel.IsReadyForInvocations);
        using CancellationTokenSource cancellation = new();
        ScriptInvocationContext first = CreateScaleInInvocation(function);
        first.CancellationToken = cancellation.Token;
        ScriptInvocationContext remaining = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryDispatchInvocation(first));
        Assert.True(selected.Channel.TryPostInvocation(remaining));
        Task sharedDrain = selected.Channel.DrainInvocationsAsync();
        if (scaleInFirst)
        {
            Assert.Equal(1, registry.ScaleInWorkers(FunctionGroups.Durable, isAlwaysReady: false, desiredWorkerCount: 0));
        }

        IRuntimeDrainManager manager = new RuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<RuntimeDrainManager>.Instance);
        RuntimeDrainReceipt receipt = manager.Accept(RuntimeDrainModel.Immediate);
        Assert.Equal(receipt, manager.Accept(RuntimeDrainModel.Immediate));
        Task runtimeDrain = selected.Channel.TryBeginRuntimeDrain();
        Assert.Same(runtimeDrain, selected.Channel.TryBeginRuntimeDrain());
        if (cancelFirst)
        {
            cancellation.Cancel();
        }

        await selected.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        if (cancelFirst)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.ResultSource.Task.WaitAsync(TestTimeout));
        }
        else
        {
            await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            await selected.Channel.InvokeResponse(new()
            {
                InvocationId = first.ExecutionContext.InvocationId.ToString(),
                Result = new()
                {
                    Status = StatusResult.Types.Status.Failure,
                    Exception = new() { Message = "Invocation failed." },
                },
            });
            await Assert.ThrowsAsync<Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException>(
                () => first.ResultSource.Task.WaitAsync(TestTimeout));
        }

        Assert.False(sharedDrain.IsCompleted);
        Assert.False(runtimeDrain.IsCompleted);
        Assert.False(remaining.ResultSource.Task.IsCompleted);
        Assert.False(selected.DisposeStarted.IsCompleted);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));

        await selected.Channel.InvokeResponse(new()
        {
            InvocationId = remaining.ExecutionContext.InvocationId.ToString(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        Assert.NotNull(await remaining.ResultSource.Task.WaitAsync(TestTimeout));
        if (cancelFirst)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sharedDrain.WaitAsync(TestTimeout));
        }
        else
        {
            await Assert.ThrowsAsync<Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException>(
                () => sharedDrain.WaitAsync(TestTimeout));
        }

        await runtimeDrain.WaitAsync(TestTimeout);
        await selected.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => selected.DisposeCount == 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RuntimeDrain_DeadlineUsesSharedShutdownAndTelemetryWithoutResetOnEscalation(
        bool functionLoaded,
        bool scaleInFirst)
    {
        Mock<TimeProvider> clock = new() { CallBase = true };
        RegistryHarness harness = new(linkTimeout: TestTimeout, timeProvider: clock.Object);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("selected");
        harness.Enqueue(selected);
        await LinkAsync(registry, selected.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await WaitUntilAsync(() => selected.Channel.IsReadyForInvocations);
        if (functionLoaded)
        {
            await selected.SendFunctionLoadResponseAsync(function.GetFunctionId());
        }

        using Activity activity = new Activity("RuntimeDrainInvocation").Start();
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        Assert.True(selected.Channel.TryPostInvocation(invocation));
        if (functionLoaded)
        {
            await selected.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        }

        if (scaleInFirst)
        {
            Assert.Equal(1, registry.ScaleInWorkers(FunctionGroups.Durable, isAlwaysReady: false, desiredWorkerCount: 0));
        }

        TimerCallback expire = null;
        object timerState = null;
        clock.Setup(provider => provider.GetTimestamp()).Returns(0);
        clock.Setup(provider => provider.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object>(), TimeSpan.FromMinutes(60), Timeout.InfiniteTimeSpan))
            .Returns((TimerCallback callback, object state, TimeSpan _, TimeSpan _) =>
            {
                expire = callback;
                timerState = state;
                return Mock.Of<ITimer>();
            });
        IRuntimeDrainManager manager = new RuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<RuntimeDrainManager>.Instance);
        RuntimeDrainReceipt phased = manager.Accept(RuntimeDrainModel.Phased);
        Assert.Equal(phased, manager.Accept(RuntimeDrainModel.Phased));
        Assert.Equal(phased.AcceptedAtUtc, manager.Accept(RuntimeDrainModel.Immediate).AcceptedAtUtc);
        clock.Verify(provider => provider.CreateTimer(
            It.IsAny<TimerCallback>(), It.IsAny<object>(), TimeSpan.FromMinutes(60), Timeout.InfiniteTimeSpan), Times.Once);
        Assert.NotNull(expire);
        Assert.False(invocation.ResultSource.Task.IsCompleted);
        Assert.False(selected.DisposeStarted.IsCompleted);

        expire(timerState);

        TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(
            () => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.Contains(selected.Id, failure.Message, StringComparison.Ordinal);
        await selected.DisposeStarted.WaitAsync(TestTimeout);
        await selected.Channel.DisposeAsync();
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(failure.Message, activity.StatusDescription);
        Assert.Single(activity.Events);
        Assert.False(selected.Channel.TryDispatchInvocation(CreateScaleInInvocation(function)));
        Assert.False(selected.Channel.IsExecutingInvocation(invocation.ExecutionContext.InvocationId.ToString()));
        Assert.Equal(1, selected.DisposeCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task RuntimeDrain_ClosesAdmissionAndReusesDrainUntilCompletionOrDeadline(
        bool completeBeforeDeadline,
        bool scaleInFirst)
    {
        TimeSpan runtimeDrainGracePeriod = completeBeforeDeadline
            ? TestTimeout
            : TimeSpan.FromMilliseconds(100);
        RegistryHarness harness = new(
            linkTimeout: TestTimeout,
            runtimeDrainGracePeriod: runtimeDrainGracePeriod);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("DurableFunction");
        ChannelControl selected = CreateClassifiedWorker("selected");
        harness.Enqueue(selected);
        await LinkAsync(registry, selected.Id);
        await registry.ActivateAsync(
            [function],
            new ManagedDependencyOptions(),
            functionTimeout: null);
        await selected.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await selected.Transport.SendResponseAsync(new()
        {
            FunctionLoadResponse = new()
            {
                FunctionId = function.GetFunctionId(),
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });
        await WaitUntilAsync(() => selected.Channel.IsReadyForInvocations);

        using RpcClientFunctionInvocationDispatcher dispatcher = new(
            registry,
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateScaleInInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await selected.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.InvocationRequest);
        if (scaleInFirst)
        {
            Assert.Equal(
                1,
                registry.ScaleInWorkers(
                    FunctionGroups.Durable,
                    isAlwaysReady: false,
                    desiredWorkerCount: 0));
        }

        IRuntimeDrainManager manager = new RuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<RuntimeDrainManager>.Instance);

        RuntimeDrainReceipt receipt = manager.Accept(
            RuntimeDrainModel.Immediate);

        Assert.Equal(RuntimeDrainModel.Immediate, receipt.DrainModel);
        Assert.False(
            selected.Channel.TryDispatchInvocation(
                CreateScaleInInvocation(function)));
        WorkerLinkException admissionClosed =
            await Assert.ThrowsAsync<WorkerLinkException>(
                () => registry.LinkAsync(
                    "new-worker",
                    CreateEndpoint("new-worker")));
        Assert.Equal(
            WorkerLinkFailureReason.RuntimeStopping,
            admissionClosed.Reason);
        Assert.False(selected.Transport.DisposeStarted.IsCompleted);

        if (completeBeforeDeadline)
        {
            await selected.Transport.SendResponseAsync(new()
            {
                InvocationResponse = new()
                {
                    InvocationId = request.InvocationRequest.InvocationId,
                    Result = new()
                    {
                        Status = StatusResult.Types.Status.Success,
                    },
                },
            });
            await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        }
        else
        {
            TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(
                () => invocation.ResultSource.Task.WaitAsync(TestTimeout));
            Assert.Contains(
                selected.Id,
                timeout.Message,
                StringComparison.Ordinal);
        }

        await selected.Transport.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => selected.DisposeCount == 1);
    }
}
