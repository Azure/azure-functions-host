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
