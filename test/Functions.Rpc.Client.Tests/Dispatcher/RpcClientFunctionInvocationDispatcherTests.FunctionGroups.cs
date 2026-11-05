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
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class RpcClientFunctionInvocationDispatcherTests
{
    [Theory]
    [InlineData("httpTrigger", "HTTP", "16", false)]
    [InlineData("eventGridTrigger", "http", "16", false)]
    [InlineData("orchestrationTrigger", "durable", null, false)]
    [InlineData("activityTrigger", "DURABLE", null, false)]
    [InlineData("entityTrigger", "durable", null, false)]
    [InlineData("queueTrigger", "function:testfunction", null, false)]
    [InlineData("timerTrigger", "function:TestFunction", null, false)]
    [InlineData("blobTrigger", "function:TestFunction", null, false)]
    [InlineData("blobTrigger", "blob", null, true)]
    public async Task InvokeAsync_SelectsOnlyAssignedFunctionGroup(string trigger, string group, string concurrency, bool eventGridBlob)
    {
        await using ClientWorkerChannelTestHarness wrong = await ClientWorkerChannelTestHarness.CreateAsync(
            "wrong", functionGroupName: eventGridBlob ? "http" : "deployment:validation", maxHttpConcurrency: eventGridBlob ? "16" : null);
        await using ClientWorkerChannelTestHarness matching = await ClientWorkerChannelTestHarness.CreateAsync(
            "matching", functionGroupName: group, maxHttpConcurrency: concurrency);
        FunctionMetadata function = CreateFunction();
        function.Bindings.Add(new()
        {
            Name = "input",
            Type = trigger,
            Direction = BindingDirection.In,
            Raw = new JObject { ["source"] = eventGridBlob ? "EventGrid" : "LogsAndContainerScan" },
        });
        await PrepareReadyChannelAsync(wrong, function);
        await PrepareReadyChannelAsync(matching, function);
        PublishReady(wrong.Channel, matching.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        await dispatcher.InvokeAsync(CreateInvocation(function));

        await matching.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        Assert.False(wrong.Transport.Requests.TryRead(out _));
        Assert.Equal(0, wrong.Channel.FunctionInputBuffers[function.GetFunctionId()].Count);
    }

    [Fact]
    public async Task InvokeAsync_RoundRobinIsIndependentForEachGroup()
    {
        await using ClientWorkerChannelTestHarness firstA = await ClientWorkerChannelTestHarness.CreateAsync("a1", functionGroupName: "function:First");
        await using ClientWorkerChannelTestHarness secondA = await ClientWorkerChannelTestHarness.CreateAsync("a2", functionGroupName: "function:First");
        await using ClientWorkerChannelTestHarness firstB = await ClientWorkerChannelTestHarness.CreateAsync("b1", functionGroupName: "function:Second");
        await using ClientWorkerChannelTestHarness secondB = await ClientWorkerChannelTestHarness.CreateAsync("b2", functionGroupName: "function:Second");
        FunctionMetadata first = new() { Name = "First", Language = "external" };
        FunctionMetadata second = new() { Name = "Second", Language = "external" };
        await PrepareReadyChannelAsync(firstA, first);
        await PrepareReadyChannelAsync(secondA, first);
        await PrepareReadyChannelAsync(firstB, second);
        await PrepareReadyChannelAsync(secondB, second);
        PublishReady(firstA.Channel, secondA.Channel, firstB.Channel, secondB.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([first, second]);

        await dispatcher.InvokeAsync(CreateInvocation(first));
        await firstA.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await dispatcher.InvokeAsync(CreateInvocation(second));
        await firstB.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await dispatcher.InvokeAsync(CreateInvocation(first));
        await secondA.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await dispatcher.InvokeAsync(CreateInvocation(second));
        await secondB.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ReadyWrongGroupWaitsForChangeWithoutPolling(bool stopDispatcher)
    {
        await using ClientWorkerChannelTestHarness wrong = await ClientWorkerChannelTestHarness.CreateAsync(
            "wrong", functionGroupName: "durable");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(wrong, function);
        PublishReady(wrong.Channel);
        using CancellationTokenSource cancellation = new();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function, cancellation.Token));

        Assert.False(invocation.IsCompleted);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
        if (stopDispatcher)
        {
            dispatcher.PreShutdown();
        }
        else
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
        Assert.False(wrong.Transport.Requests.TryRead(out _));
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_RoundRobinCursorHandlesShrinkingGroup()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("first");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("second");
        await using ClientWorkerChannelTestHarness third = await ClientWorkerChannelTestHarness.CreateAsync("third");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        await PrepareReadyChannelAsync(third, function);
        PublishReady(first.Channel, second.Channel, third.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        await dispatcher.InvokeAsync(CreateInvocation(function));
        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await dispatcher.InvokeAsync(CreateInvocation(function));
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        _readyChannels.Remove(third.Channel);
        await dispatcher.InvokeAsync(CreateInvocation(function));
        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await dispatcher.InvokeAsync(CreateInvocation(function));
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        Assert.False(third.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task TryGetReadyChannel_BlockedGroupDoesNotBlockAnotherGroup()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("first", functionGroupName: "function:First");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("second", functionGroupName: "function:Second");
        await PrepareReadyChannelAsync(first, new() { Name = "First", Language = "external" });
        await PrepareReadyChannelAsync(second, new() { Name = "Second", Language = "external" });
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        using ManualResetEventSlim release = new(false);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IReadOnlyList<WorkerChannel>> blockedSnapshot = new(MockBehavior.Strict);
        blockedSnapshot.SetupGet(channels => channels.Count).Returns(2);
        blockedSnapshot.Setup(channels => channels[0]).Returns(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TestTimeout))
            {
                throw new TimeoutException("Selection was not released.");
            }

            return first.Channel;
        });
        Task<WorkerChannel> blockedSelection = Task.Run(() =>
        {
            Assert.True(dispatcher.TryGetReadyChannel(blockedSnapshot.Object, "function:First", out WorkerChannel selected));
            return selected;
        });
        Task<WorkerChannel> otherSelection = Task.FromResult<WorkerChannel>(null);

        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            otherSelection = Task.Run(() =>
            {
                Assert.True(dispatcher.TryGetReadyChannel([first.Channel, second.Channel], "function:Second", out WorkerChannel selected));
                return selected;
            });

            Assert.Same(second.Channel, await otherSelection.WaitAsync(TestTimeout));
            Assert.False(blockedSelection.IsCompleted);
        }
        finally
        {
            release.Set();
            await Task.WhenAll(blockedSelection, otherSelection).WaitAsync(TestTimeout);
        }

        Assert.Same(first.Channel, await blockedSelection);
    }

    [Fact]
    public async Task TryGetReadyChannel_ConcurrentSelectionsKeepIndependentCaseInsensitiveRotation()
    {
        List<ClientWorkerChannelTestHarness> workers = [];
        try
        {
            for (int i = 0; i < 8; i++)
            {
                string group = $"function:Group{i % 4}";
                ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync($"worker-{i}", functionGroupName: group);
                workers.Add(worker);
                await PrepareReadyChannelAsync(worker, new() { Name = $"Group{i % 4}", Language = "external" });
            }

            WorkerChannel[] snapshot = workers.ConvertAll(worker => (WorkerChannel)worker.Channel).ToArray();
            using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
            int[] selections = new int[snapshot.Length];
            Parallel.For(0, 4_000, i =>
            {
                int group = i % 4;
                string requestedGroup = i % 8 < 4 ? $"function:Group{group}" : $"FUNCTION:GROUP{group}";
                Assert.True(dispatcher.TryGetReadyChannel(snapshot, requestedGroup, out WorkerChannel selected));
                int selectedIndex = Array.IndexOf(snapshot, selected);
                Assert.Equal(group, selectedIndex % 4);
                Interlocked.Increment(ref selections[selectedIndex]);
            });

            Assert.All(selections, count => Assert.Equal(500, count));
        }
        finally
        {
            foreach (ClientWorkerChannelTestHarness worker in workers)
            {
                await worker.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task TryGetReadyChannel_UnrelatedWorkerPositionsDoNotBiasRotation()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("first");
        await using ClientWorkerChannelTestHarness other = await ClientWorkerChannelTestHarness.CreateAsync("other", functionGroupName: "durable");
        await using ClientWorkerChannelTestHarness last = await ClientWorkerChannelTestHarness.CreateAsync("last");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(other, function);
        await PrepareReadyChannelAsync(last, function);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        WorkerChannel[] snapshot = [first.Channel, other.Channel, other.Channel, last.Channel];
        string group = function.GetFunctionGroup();

        for (int i = 0; i < 6; i++)
        {
            Assert.True(dispatcher.TryGetReadyChannel(snapshot, group, out WorkerChannel selected));
            Assert.Same(i % 2 == 0 ? first.Channel : last.Channel, selected);
        }
    }

    [Fact]
    public async Task TryGetReadyChannel_WrapsAndSkipsWorkerThatStopsBeforeSelection()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("first");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("second");
        await using ClientWorkerChannelTestHarness third = await ClientWorkerChannelTestHarness.CreateAsync("third");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        await PrepareReadyChannelAsync(third, function);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        string group = function.GetFunctionGroup();
        WorkerChannel[] initialSnapshot = [first.Channel, second.Channel, third.Channel];
        Assert.True(dispatcher.TryGetReadyChannel(initialSnapshot, group, out WorkerChannel initialFirst));
        Assert.Same(first.Channel, initialFirst);
        Assert.True(dispatcher.TryGetReadyChannel(initialSnapshot, group, out WorkerChannel initialSecond));
        Assert.Same(second.Channel, initialSecond);
        Mock<IReadOnlyList<WorkerChannel>> snapshot = new(MockBehavior.Strict);
        snapshot.SetupGet(channels => channels.Count).Returns(3);
        snapshot.Setup(channels => channels[0]).Returns(first.Channel);
        snapshot.Setup(channels => channels[1]).Returns(second.Channel);
        snapshot.Setup(channels => channels[2]).Returns(() =>
        {
            third.Channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return third.Channel;
        });

        Assert.True(dispatcher.TryGetReadyChannel(snapshot.Object, group, out WorkerChannel selected));
        Assert.Same(first.Channel, selected);

        snapshot.Verify(channels => channels[2], Times.Once);
        snapshot.Verify(channels => channels[0], Times.Once);
        snapshot.Verify(channels => channels[1], Times.Never);
        first.Channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        second.Channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        snapshot.Invocations.Clear();

        Assert.False(dispatcher.TryGetReadyChannel(snapshot.Object, group, out WorkerChannel missing));
        Assert.Null(missing);
        snapshot.Verify(channels => channels[0], Times.Once);
        snapshot.Verify(channels => channels[1], Times.Once);
        snapshot.Verify(channels => channels[2], Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryGetReadyChannel_DoesNotAllocateBeyondExistingReadinessChecks(bool multipleMatches)
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("first");
        await using ClientWorkerChannelTestHarness other = await ClientWorkerChannelTestHarness.CreateAsync(
            "other", functionGroupName: multipleMatches ? "function:TestFunction" : "durable");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(other, function);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        WorkerChannel[] snapshot = [first.Channel, other.Channel];
        string group = function.GetFunctionGroup();
        for (int i = 0; i < 10; i++)
        {
            Assert.True(dispatcher.TryGetReadyChannel(snapshot, group, out _));
        }

        // Debug builds can box enum flags in shared readiness checks; measure that existing cost separately.
        bool allReady = true;
        int readinessChecks = multipleMatches ? 1_000 : 2_000;
        long beforeReadiness = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < readinessChecks; i++)
        {
            allReady &= first.Channel.IsReadyForInvocations;
        }

        long readinessAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeReadiness;
        bool allSelected = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            allSelected &= dispatcher.TryGetReadyChannel(snapshot, group, out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allReady);
        Assert.True(allSelected);
        Assert.Equal(readinessAllocated, allocated);
    }

    [Fact]
    public async Task InvokeAsync_MatchingLinkWakesWrongGroupWait()
    {
        await using ClientWorkerChannelTestHarness wrong = await ClientWorkerChannelTestHarness.CreateAsync("wrong", functionGroupName: "durable");
        await using ClientWorkerChannelTestHarness matching = await ClientWorkerChannelTestHarness.CreateAsync("matching");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(wrong, function);
        await PrepareReadyChannelAsync(matching, function);
        PublishReady(wrong.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function));
        Assert.False(invocation.IsCompleted);

        PublishReady(matching.Channel);
        _changed.TrySetResult();
        await invocation.WaitAsync(TestTimeout);

        await matching.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        Assert.False(wrong.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task InvokeAsync_MatchingLinkDuringSamplingIsNotLost()
    {
        await using ClientWorkerChannelTestHarness wrong = await ClientWorkerChannelTestHarness.CreateAsync("wrong", functionGroupName: "durable");
        await using ClientWorkerChannelTestHarness matching = await ClientWorkerChannelTestHarness.CreateAsync("matching");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(wrong, function);
        await PrepareReadyChannelAsync(matching, function);
        _registry.SetupSequence(registry => registry.GetReadyChannelsAsync())
            .Returns(() =>
            {
                _registry.Verify(registry => registry.GetChangeTask(), Times.Once);
                _changed.TrySetResult();
                return new ValueTask<IReadOnlyList<WorkerChannel>>([wrong.Channel]);
            })
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>([matching.Channel]));
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        await dispatcher.InvokeAsync(CreateInvocation(function)).WaitAsync(TestTimeout);

        await matching.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task InvokeAsync_NoMatchingGroupTimesOutWithoutPolling()
    {
        await using ClientWorkerChannelTestHarness wrong = await ClientWorkerChannelTestHarness.CreateAsync("wrong", functionGroupName: "durable");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(wrong, function);
        PublishReady(wrong.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(50));
        await dispatcher.InitializeAsync([function]);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.InvokeAsync(CreateInvocation(function)));

        Assert.Contains("function:TestFunction", exception.Message, StringComparison.Ordinal);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
        Assert.False(wrong.Transport.Requests.TryRead(out _));
    }
}
