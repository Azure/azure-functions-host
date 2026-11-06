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
using Microsoft.Azure.WebJobs.Script.Grpc;
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
    public async Task IdleMonitor_RetiresEligibleWorkerOnOneMinuteScan()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        await ReadyIdleWorkerAsync(registry, worker, CreateIdleFunction("Function"));
        time.Advance(TimeSpan.FromMinutes(5));
        TaskCompletionSource scanStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<TimeProvider> clock = new();
        clock.Setup(provider => provider.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(),
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object state, TimeSpan due, TimeSpan period) =>
            {
                ITimer timer = time.CreateTimer(callback, state, due, period);
                scanStarted.TrySetResult();
                return timer;
            });
        using HttpWorkerIdleMonitor monitor = new(registry, clock.Object);
        await monitor.StartAsync(CancellationToken.None);
        try
        {
            await scanStarted.Task.WaitAsync(TestTimeout);
            time.Advance(TimeSpan.FromSeconds(59));
            Assert.Equal(0, worker.DisposeCount);
            time.Advance(TimeSpan.FromSeconds(1));
            await worker.DisposeStarted.WaitAsync(TestTimeout);
            Assert.Empty(registry.GetInitializedChannels());
            Assert.False(worker.Channel.IsReadyForInvocations);
        }
        finally
        {
            await monitor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task IdleRetirement_RejectsNegativeThreshold()
    {
        RegistryHarness harness = new(TimeSpan.FromSeconds(30));
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => registry.RetireIdleHttpWorkersAsync(TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public async Task IdleRetirement_UnusedWorkerGetsFiveMinuteGraceWithoutWaitingForFunctionLoads()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker");
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([CreateIdleFunction("Function")], new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);

        time.Advance(TimeSpan.FromMinutes(5));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(0, worker.DisposeCount);

        time.Advance(TimeSpan.FromTicks(1));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.False(worker.Channel.IsReadyForInvocations);
    }

    [Theory]
    [InlineData("http", "True")]
    [InlineData("function:timer", "False")]
    public async Task IdleRetirement_ExemptsAlwaysReadyAndNonHttpWorkers(string group, string alwaysReady)
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker", group, alwaysReady);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        await ReadyIdleWorkerAsync(registry, worker, CreateIdleFunction("Function"));

        time.Advance(TimeSpan.FromMinutes(6));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);

        Assert.Equal(0, worker.DisposeCount);
        Assert.Same(worker.Channel, Assert.Single(registry.GetInitializedChannels()));
    }

    [Theory]
    [InlineData("False")]
    [InlineData(null)]
    [InlineData("not-a-bool")]
    public async Task IdleRetirement_QueuedAndOverlappingRequestsResetTwoMinuteIdleTime(string alwaysReady)
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        ChannelControl worker = CreateIdleWorker("worker", alwaysReady: alwaysReady);
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateIdleFunction("Function");
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        using RpcClientFunctionInvocationDispatcher dispatcher = new(registry, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        await dispatcher.InitializeAsync([function]);
        await dispatcher.InvokeAsync(CreateInvocation(function));
        await dispatcher.InvokeAsync(CreateInvocation(function));

        time.Advance(TimeSpan.FromMinutes(6));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(2, worker.Channel.OngoingRequests);
        Assert.Equal(0, worker.DisposeCount);
        Assert.False(worker.Transport.Requests.TryRead(out _));

        CompleteFunctionLoad(worker, function);
        StreamingMessage first = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        StreamingMessage second = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.Channel.InvokeResponse(new()
        {
            InvocationId = first.InvocationRequest.InvocationId,
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 1);
        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(0, worker.DisposeCount);

        await worker.Channel.InvokeResponse(new()
        {
            InvocationId = second.InvocationRequest.InvocationId,
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
    public async Task IdleRetirement_WaitsForStreamingBodyAndResponseCompletionDespiteCancellation()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: time);
        TaskCompletionSource<ForwarderError> body = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHttpProxyService> proxy = new();
        proxy.Setup(service => service.StartForwarding(It.IsAny<ScriptInvocationContext>(), It.IsAny<Uri>()))
            .Callback((ScriptInvocationContext context, Uri _) => context.Properties[ScriptConstants.HttpProxyTask] = body.Task);
        proxy.Setup(service => service.EnsureSuccessfulForwardingAsync(It.IsAny<ScriptInvocationContext>()))
            .Returns(body.Task);
        ChannelControl worker = new("streaming")
        {
            Capabilities = new Dictionary<string, string>
            {
                ["FunctionGroupName"] = "http",
                ["IsAlwaysReady"] = "False",
                ["MaxHttpConcurrency"] = "16",
                ["HttpUri"] = "https://worker:8080/",
            },
            HttpProxyService = proxy.Object,
        };
        harness.Enqueue(worker);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateIdleFunction("Stream");
        await ReadyIdleWorkerAsync(registry, worker, function);
        ScriptInvocationContext invocation = CreateInvocation(function);
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
        using RpcClientFunctionInvocationDispatcher dispatcher = new(registry, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
        await dispatcher.InitializeAsync([function]);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        cancellation.Cancel();
        Task processing = worker.Channel.InvokeResponse(new()
        {
            InvocationId = request.InvocationRequest.InvocationId,
            Result = new() { Status = StatusResult.Types.Status.Success },
        });

        time.Advance(TimeSpan.FromMinutes(6));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.Channel.OngoingRequests);
        Assert.Equal(0, worker.DisposeCount);

        body.SetResult(ForwarderError.None);
        await processing.WaitAsync(TestTimeout);
        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.Channel.OngoingRequests);
        Assert.Equal(0, worker.DisposeCount);

        await completeResponse(completionState);
        await WaitUntilAsync(() => worker.Channel.OngoingRequests == 0);
        time.Advance(TimeSpan.FromMinutes(3));
        await registry.RetireIdleHttpWorkersAsync(IdleThreshold);
        Assert.Equal(1, worker.DisposeCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task IdleRetirement_WithdrawsBeforeBoundedDisposalAndRejectsStaleDispatch(bool cancel, bool failLate)
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        TaskCompletionSource disposalWaitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<TimeProvider> clock = new();
        clock.Setup(provider => provider.GetUtcNow()).Returns(time.GetUtcNow);
        clock.Setup(provider => provider.GetTimestamp()).Returns(time.GetTimestamp);
        clock.SetupGet(provider => provider.TimestampFrequency).Returns(time.TimestampFrequency);
        clock.Setup(provider => provider.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(),
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimerCallback callback, object state, TimeSpan due, TimeSpan period) =>
            {
                ITimer timer = time.CreateTimer(callback, state, due, period);
                if (due == TimeSpan.FromSeconds(10))
                {
                    disposalWaitStarted.TrySetResult();
                }

                return timer;
            });
        RegistryHarness harness = new(TimeSpan.FromSeconds(30), timeProvider: clock.Object);
        ChannelControl worker = new("retiring", blockDisposal: true)
        {
            Capabilities = new Dictionary<string, string>
            {
                ["FunctionGroupName"] = "http",
                ["IsAlwaysReady"] = "False",
                ["MaxHttpConcurrency"] = "16",
            },
        };
        ChannelControl other = CreateIdleWorker("other");
        ChannelControl replacement = CreateIdleWorker("replacement", alwaysReady: "True");
        harness.Enqueue(worker);
        harness.Enqueue(other);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateIdleFunction("Function");
        await ReadyIdleWorkerAsync(registry, worker, function);
        await LinkAsync(registry, other.Id);
        await other.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        CompleteFunctionLoad(other, function);
        await LinkAsync(registry, replacement.Id);
        await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        CompleteFunctionLoad(replacement, function);
        time.Advance(TimeSpan.FromMinutes(6));
        using CancellationTokenSource cancellation = new();
        Task retirement = registry.RetireIdleHttpWorkersAsync(IdleThreshold, cancellation.Token);
        await worker.DisposeStarted.WaitAsync(TestTimeout);
        try
        {
            await other.DisposeStarted.WaitAsync(TestTimeout);
            Assert.Equal(1, other.DisposeCount);
            Assert.False(other.Channel.IsReadyForInvocations);
            Mock<IWorkerChannelRegistry> staleRegistry = new();
            staleRegistry.SetupSequence(value => value.GetReadyChannelsAsync())
                .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>([worker.Channel, other.Channel]))
                .Returns(() => registry.GetReadyChannelsAsync());
            using RpcClientFunctionInvocationDispatcher dispatcher = new(staleRegistry.Object, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance);
            await dispatcher.InitializeAsync([function]);
            await dispatcher.InvokeAsync(CreateInvocation(function));
            await replacement.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            Assert.Same(replacement.Channel, Assert.Single(registry.GetInitializedChannels()));
            Assert.Equal(0, worker.Channel.OngoingRequests);
            Assert.False(retirement.IsCompleted);

            Task<bool> unlink = registry.UnlinkAsync(worker.Id);
            Assert.False(unlink.IsCompleted);
            await disposalWaitStarted.Task.WaitAsync(TestTimeout);
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retirement.WaitAsync(TestTimeout));
            }
            else
            {
                time.Advance(TimeSpan.FromSeconds(10));
                await retirement.WaitAsync(TestTimeout);
            }

            Assert.False(await unlink.WaitAsync(TestTimeout));
            await registry.RetireIdleHttpWorkersAsync(IdleThreshold).WaitAsync(TestTimeout);
        }
        finally
        {
            if (failLate)
            {
                IOException failure = new("Late disposal failure.");
                worker.Transport.FailDispose(failure);
                IOException observed = await Assert.ThrowsAsync<IOException>(
                    () => worker.Channel.DisposeAsync().AsTask().WaitAsync(TestTimeout));
                Assert.Same(failure, observed);
                await WaitUntilAsync(() => harness.Logger.Invocations.Any(call => call.Arguments.Contains(failure)));
                harness.Logger.Verify(logger => logger.Log(
                    LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    failure, It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            }
            else
            {
                worker.AllowDispose();
                await worker.Channel.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            }
            if (!cancel || !cancellation.IsCancellationRequested)
            {
                await retirement.WaitAsync(TestTimeout);
            }
        }
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

    private static FunctionMetadata CreateIdleFunction(string name)
    {
        FunctionMetadata function = CreateFunction(name);
        function.Bindings.Add(new() { Name = "req", Type = "httpTrigger", Direction = BindingDirection.In });
        return function;
    }

    private static async Task ReadyIdleWorkerAsync(WorkerChannelRegistry registry, ChannelControl worker, FunctionMetadata function)
    {
        await LinkAsync(registry, worker.Id);
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        CompleteFunctionLoad(worker, function);
    }

    private static void CompleteFunctionLoad(ChannelControl worker, FunctionMetadata function)
        => worker.Channel.LoadResponse(new FunctionLoadResponse
        {
            FunctionId = function.GetFunctionId(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });
}
