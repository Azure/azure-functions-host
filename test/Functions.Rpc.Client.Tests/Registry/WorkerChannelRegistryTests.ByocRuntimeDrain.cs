// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Fact]
    public async Task ByocDrain_PhasedUsesFrozenFiveBatchScheduleAndHttpCutoff()
    {
        DrainTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        List<ChannelControl> nonHttpWorkers = [.. Enumerable.Range(0, 21)
            .Select(index => new ChannelControl(
                $"non-http-{index:D2}",
                blockDisposal: true,
                functionGroupName: $"function-{index:D2}"))];
        ChannelControl httpWorker = new(
            "http",
            blockDisposal: true,
            functionGroupName: FunctionGroups.Http);
        foreach (ChannelControl worker in nonHttpWorkers.Append(httpWorker))
        {
            harness.Enqueue(worker);
        }

        await using WorkerChannelRegistry registry = harness.Registry;
        foreach (ChannelControl worker in nonHttpWorkers.Append(httpWorker))
        {
            await LinkAsync(registry, worker.Id);
        }

        ByocRuntimeDrainManager manager = new(
            registry,
            timeProvider,
            NullLogger<ByocRuntimeDrainManager>.Instance);
        ByocRuntimeDrainReceipt receipt = manager.Accept(ByocDrainModel.Phased);

        Assert.Equal(ByocDrainModel.Phased, receipt.DrainModel);
        await AssertSelectedAsync(nonHttpWorkers.Take(5));
        AssertNotSelected(nonHttpWorkers.Skip(5).Append(httpWorker));

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await AssertSelectedAsync(nonHttpWorkers.Take(10));
        AssertNotSelected(nonHttpWorkers.Skip(10).Append(httpWorker));

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await AssertSelectedAsync(nonHttpWorkers.Take(15));
        AssertNotSelected(nonHttpWorkers.Skip(15).Append(httpWorker));

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await AssertSelectedAsync(nonHttpWorkers.Take(20));
        AssertNotSelected(nonHttpWorkers.Skip(20).Append(httpWorker));

        timeProvider.Advance(TimeSpan.FromSeconds(4));
        Assert.False(httpWorker.DisposeStarted.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await httpWorker.DisposeStarted.WaitAsync(TestTimeout);
        Assert.False(nonHttpWorkers[^1].DisposeStarted.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await nonHttpWorkers[^1].DisposeStarted.WaitAsync(TestTimeout);

        foreach (ChannelControl worker in nonHttpWorkers.Append(httpWorker))
        {
            worker.AllowDispose();
        }

        await manager.WaitForSelectedWorkerDrainAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ByocDrain_ImmediateSelectsEveryWorkerAndClosesNewLinks()
    {
        DrainTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl[] workers =
        [
            new("timer-1", blockDisposal: true, functionGroupName: "timer"),
            new("timer-2", blockDisposal: true, functionGroupName: "timer"),
            new("http-1", blockDisposal: true, functionGroupName: FunctionGroups.Http),
            new("http-2", blockDisposal: true, functionGroupName: FunctionGroups.Http),
        ];
        ChannelControl closingWorker = new(
            "closing",
            blockDisposal: true,
            functionGroupName: "timer");
        ChannelControl initializingWorker = new(
            "initializing",
            blockStart: true,
            blockDisposal: true,
            functionGroupName: "timer");
        foreach (ChannelControl worker in workers
            .Append(closingWorker)
            .Append(initializingWorker))
        {
            harness.Enqueue(worker);
        }

        await using WorkerChannelRegistry registry = harness.Registry;
        try
        {
            IHostStateCoordinator stateProvider = registry;
            IRuntimeWorkerAdmissionGate admissionGate = registry;
            foreach (ChannelControl worker in workers.Append(closingWorker))
            {
                await LinkAsync(registry, worker.Id);
            }

            Task<WorkerLinkResult> initializingLink = registry.LinkAsync(
                initializingWorker.Id,
                CreateEndpoint(initializingWorker.Id));
            await initializingWorker.StartEntered.WaitAsync(TestTimeout);
            Task<bool> closingUnlink = registry.UnlinkAsync(closingWorker.Id);
            await closingWorker.DisposeStarted.WaitAsync(TestTimeout);
            HostStateSnapshot initialState = stateProvider.Current;
            Assert.Equal(workers.Length + 2, initialState.LinkedWorkerCount);
            ByocRuntimeDrainManager manager = new(
                registry,
                timeProvider,
                NullLogger<ByocRuntimeDrainManager>.Instance);
            manager.Accept(ByocDrainModel.Immediate);

            await AssertSelectedAsync(workers);
            WorkerLinkException rejection = await Assert.ThrowsAsync<WorkerLinkException>(
                () => registry.LinkAsync("late-worker", CreateEndpoint("late-worker")));
            Assert.Equal(WorkerLinkFailureReason.RuntimeStopping, rejection.Reason);

            HostStateSnapshot admissionState = stateProvider.Current;
            Assert.True(admissionGate.IsClosed);
            Assert.Equal(workers.Length + 2, admissionState.LinkedWorkerCount);
            Assert.Equal(0, admissionState.HttpCapacity);
            Assert.True(admissionState.Version > initialState.Version);
            Assert.True(admissionState.CreatedTime > initialState.CreatedTime);

            initializingWorker.AllowStart();
            await initializingWorker.DisposeStarted.WaitAsync(TestTimeout);
            ChannelControl[] closingOrder =
                [closingWorker, initializingWorker, .. workers];
            HostStateSnapshot previousState = admissionState;
            for (int index = 0; index < closingOrder.Length; index++)
            {
                long version = stateProvider.Current.Version;
                Task<long> stateChanged = stateProvider.WaitForChangeAsync(version);
                closingOrder[index].AllowDispose();
                await stateChanged.WaitAsync(TestTimeout);

                HostStateSnapshot linkClosedState = stateProvider.Current;
                Assert.Equal(
                    closingOrder.Length - index - 1,
                    linkClosedState.LinkedWorkerCount);
                Assert.True(linkClosedState.Version > version);
                Assert.True(
                    linkClosedState.CreatedTime > previousState.CreatedTime);
                previousState = linkClosedState;
            }

            Assert.True(await closingUnlink.WaitAsync(TestTimeout));
            WorkerLinkException initializingRejection =
                await Assert.ThrowsAsync<WorkerLinkException>(
                    () => initializingLink.WaitAsync(TestTimeout));
            Assert.Equal(
                WorkerLinkFailureReason.RuntimeStopping,
                initializingRejection.Reason);
            await manager.WaitForSelectedWorkerDrainAsync().WaitAsync(TestTimeout);
            HostStateSnapshot finalState = stateProvider.Current;
            Assert.True(admissionGate.IsClosed);
            Assert.Equal(0, finalState.LinkedWorkerCount);
            Assert.Equal(0, finalState.HttpCapacity);
            Assert.True(
                finalState.Version - initialState.Version >=
                    closingOrder.Length + 1);
            Assert.True(finalState.CreatedTime > initialState.CreatedTime);
        }
        finally
        {
            initializingWorker.AllowStart();
            foreach (ChannelControl worker in workers
                .Append(closingWorker)
                .Append(initializingWorker))
            {
                worker.AllowDispose();
            }
        }
    }

    [Fact]
    public async Task ByocDrain_SelectionFencesIntakeAndWaitsForRpcAndHttpCompletion()
    {
        DrainTimeProvider timeProvider = new();
        TaskCompletionSource httpForwarding =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> httpProxy = new(MockBehavior.Strict);
        httpProxy.Setup(service => service.StartForwarding(
                It.IsAny<ScriptInvocationContext>(),
                It.IsAny<Uri>()))
            .Callback<ScriptInvocationContext, Uri>((context, _) =>
                context.Properties[ScriptConstants.HttpProxyTask] =
                    httpForwarding.Task);
        httpProxy.Setup(service => service.EnsureSuccessfulForwardingAsync(
                It.IsAny<ScriptInvocationContext>()))
            .Returns(Task.CompletedTask);
        RegistryHarness harness = new(timeProvider);
        ChannelControl worker = new(
            "http",
            blockDisposal: true,
            functionGroupName: FunctionGroups.Http,
            httpProxyService: httpProxy.Object,
            advertisedHttpUri: "http://http.test:8080");
        ChannelControl queuedWorker = new(
            "queued",
            blockDisposal: true,
            functionGroupName: "timer");
        harness.Enqueue(worker);
        harness.Enqueue(queuedWorker);

        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, worker.Id);
        await LinkAsync(registry, queuedWorker.Id);
        FunctionMetadata function = CreateHttpFunction();
        await registry.ActivateAsync(
            [function],
            new ManagedDependencyOptions(),
            functionTimeout: null);
        StreamingMessage loadRequest = await worker.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await queuedWorker.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.Transport.SendResponseAsync(new StreamingMessage
        {
            RequestId = loadRequest.RequestId,
            FunctionLoadResponse = new()
            {
                FunctionId = function.GetFunctionId(),
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });
        ScriptInvocationContext queuedInvocation = CreateInvocation(function);
        Assert.True(
            queuedWorker.Channel.FunctionInputBuffers[function.GetFunctionId()]
                .Post(queuedInvocation));
        RpcClientFunctionInvocationDispatcher dispatcher = new(
            registry,
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance,
            TimeSpan.FromSeconds(1));
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateInvocation(function);

        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.InvocationRequest);
        ByocRuntimeDrainManager manager = new(
            registry,
            timeProvider,
            NullLogger<ByocRuntimeDrainManager>.Instance);
        manager.Accept(ByocDrainModel.Immediate);

        Assert.False(worker.DisposeStarted.IsCompleted);
        Assert.False(worker.Channel.TryDispatchInvocation(
            CreateInvocation(function)));
        InvalidOperationException queuedFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => queuedInvocation.ResultSource.Task);
        Assert.Contains(
            "before the invocation committed",
            queuedFailure.Message,
            StringComparison.Ordinal);
        await queuedWorker.DisposeStarted.WaitAsync(TestTimeout);

        await worker.Transport.SendResponseAsync(new StreamingMessage
        {
            InvocationResponse = new()
            {
                InvocationId = request.InvocationRequest.InvocationId,
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });
        await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        Assert.False(worker.DisposeStarted.IsCompleted);

        httpForwarding.SetResult();
        await worker.DisposeStarted.WaitAsync(TestTimeout);
        worker.AllowDispose();
        queuedWorker.AllowDispose();
        await manager.WaitForSelectedWorkerDrainAsync().WaitAsync(TestTimeout);
        httpProxy.VerifyAll();
    }

    [Fact]
    public async Task ByocDrain_RetryAndEscalationKeepOriginalDeadlineAndRetireWithoutReuseGrace()
    {
        DrainTimeProvider timeProvider = new();
        RegistryHarness harness = new(timeProvider);
        ChannelControl nonHttpWorker = new(
            "timer",
            blockDisposal: true,
            functionGroupName: "timer");
        ChannelControl httpWorker = new(
            "http",
            blockDisposal: true,
            functionGroupName: FunctionGroups.Http);
        harness.Enqueue(nonHttpWorker);
        harness.Enqueue(httpWorker);

        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, nonHttpWorker.Id);
        await LinkAsync(registry, httpWorker.Id);
        FunctionMetadata function = CreateFunction("TimerFunction");
        await ActivateWorkerAsync(registry, nonHttpWorker, function);
        RpcClientFunctionInvocationDispatcher dispatcher = new(
            registry,
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance,
            TimeSpan.FromSeconds(1));
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        await nonHttpWorker.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.InvocationRequest);

        ByocRuntimeDrainManager manager = new(
            registry,
            timeProvider,
            NullLogger<ByocRuntimeDrainManager>.Instance);
        ByocRuntimeDrainReceipt phased = manager.Accept(ByocDrainModel.Phased);
        ByocRuntimeDrainReceipt replay = manager.Accept(ByocDrainModel.Phased);

        Assert.Equal(phased, replay);
        Assert.False(nonHttpWorker.DisposeStarted.IsCompleted);
        Assert.False(httpWorker.DisposeStarted.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        ByocRuntimeDrainReceipt immediate = manager.Accept(
            ByocDrainModel.Immediate);
        ByocRuntimeDrainReceipt immediateReplay = manager.Accept(
            ByocDrainModel.Immediate);

        Assert.Equal(phased.AcceptedAtUtc, immediate.AcceptedAtUtc);
        Assert.Equal(immediate, immediateReplay);
        await httpWorker.DisposeStarted.WaitAsync(TestTimeout);
        Assert.False(nonHttpWorker.DisposeStarted.IsCompleted);
        Assert.Throws<ByocRuntimeDrainConflictException>(
            () => manager.Accept(ByocDrainModel.Phased));

        timeProvider.Advance(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(29));
        Assert.False(nonHttpWorker.DisposeStarted.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await nonHttpWorker.DisposeStarted.WaitAsync(TestTimeout);
        await Assert.ThrowsAsync<TimeoutException>(
            () => invocation.ResultSource.Task.WaitAsync(TestTimeout));

        httpWorker.AllowDispose();
        nonHttpWorker.AllowDispose();
        await manager.WaitForSelectedWorkerDrainAsync().WaitAsync(TestTimeout);

        WorkerLinkException rejection = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync("replacement", CreateEndpoint("replacement")));
        Assert.Equal(WorkerLinkFailureReason.RuntimeStopping, rejection.Reason);
    }

    private static async Task ActivateWorkerAsync(
        WorkerChannelRegistry registry,
        ChannelControl worker,
        FunctionMetadata function)
    {
        await registry.ActivateAsync(
            [function],
            new ManagedDependencyOptions(),
            functionTimeout: null);
        StreamingMessage request = await worker.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.Transport.SendResponseAsync(new StreamingMessage
        {
            RequestId = request.RequestId,
            FunctionLoadResponse = new()
            {
                FunctionId = function.GetFunctionId(),
                Result = new() { Status = StatusResult.Types.Status.Success },
            },
        });
    }

    private static async Task AssertSelectedAsync(
        IEnumerable<ChannelControl> workers)
    {
        foreach (ChannelControl worker in workers)
        {
            await worker.DisposeStarted.WaitAsync(TestTimeout);
        }
    }

    private static void AssertNotSelected(
        IEnumerable<ChannelControl> workers)
    {
        Assert.All(
            workers,
            worker => Assert.False(worker.DisposeStarted.IsCompleted));
    }

    private static FunctionMetadata CreateHttpFunction()
    {
        FunctionMetadata function = CreateFunction("HttpFunction");
        function.Bindings.Add(new()
        {
            Name = "req",
            Type = "httpTrigger",
            Direction = BindingDirection.In,
        });

        return function;
    }

    private static ScriptInvocationContext CreateInvocation(
        FunctionMetadata function)
    {
        ScriptInvocationContext invocation = new()
        {
            FunctionMetadata = function,
            ExecutionContext = new()
            {
                FunctionName = function.Name,
                InvocationId = Guid.NewGuid(),
            },
            BindingData = [],
            Inputs = [],
            ResultSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously),
            AsyncExecutionContext = ExecutionContext.Capture(),
            Logger = NullLogger.Instance,
        };
        if (function.Bindings.Any(binding => string.Equals(
            binding.Type,
            "httpTrigger",
            StringComparison.OrdinalIgnoreCase)))
        {
            HttpRequest request = new DefaultHttpContext().Request;
            request.Method = "GET";
            request.Host = new("test.example");
            request.Path = "/api/test";
            request.Body = Stream.Null;
            invocation.Inputs = [("req", DataType.String, request)];
        }

        return invocation;
    }

    private sealed class DrainTimeProvider : TimeProvider
    {
        private readonly Lock _lock = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_lock)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ManualTimer timer = new(
                this,
                callback,
                state,
                dueTime,
                period);
            lock (_lock)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            List<(TimerCallback Callback, object State)> callbacks = [];
            lock (_lock)
            {
                _utcNow += duration;
                _timestamp = checked(_timestamp + duration.Ticks);
                foreach (ManualTimer timer in _timers.ToArray())
                {
                    timer.CollectCallbacksLocked(_timestamp, callbacks);
                }
            }

            foreach ((TimerCallback callback, object state) in callbacks)
            {
                callback(state);
            }
        }

        private bool ChangeTimer(
            ManualTimer timer,
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (_lock)
            {
                return timer.ChangeLocked(_timestamp, dueTime, period);
            }
        }

        private void RemoveTimer(ManualTimer timer)
        {
            lock (_lock)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly DrainTimeProvider _owner;
            private readonly object _state;
            private long _dueTimestamp;
            private bool _disposed;
            private TimeSpan _period;

            public ManualTimer(
                DrainTimeProvider owner,
                TimerCallback callback,
                object state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                ChangeLocked(owner._timestamp, dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
                => _owner.ChangeTimer(this, dueTime, period);

            public void Dispose()
            {
                _disposed = true;
                _owner.RemoveTimer(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void CollectCallbacksLocked(
                long timestamp,
                List<(TimerCallback Callback, object State)> callbacks)
            {
                if (_disposed ||
                    _dueTimestamp == long.MaxValue ||
                    timestamp < _dueTimestamp)
                {
                    return;
                }

                callbacks.Add((_callback, _state));
                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _dueTimestamp = long.MaxValue;
                    return;
                }

                _dueTimestamp = checked(_dueTimestamp + _period.Ticks);
            }

            public bool ChangeLocked(
                long timestamp,
                TimeSpan dueTime,
                TimeSpan period)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : checked(timestamp + dueTime.Ticks);
                return true;
            }
        }
    }
}
