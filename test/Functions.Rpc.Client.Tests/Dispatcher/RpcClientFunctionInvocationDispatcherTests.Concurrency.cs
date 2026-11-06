// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class RpcClientFunctionInvocationDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_FallbackScansRemainingWorkersWithoutAdvancingPreference()
    {
        await using ClientWorkerChannelTestHarness first = await CreateLimitedWorkerAsync("first", 1);
        await using ClientWorkerChannelTestHarness second = await CreateLimitedWorkerAsync("second", 1);
        await using ClientWorkerChannelTestHarness third = await CreateLimitedWorkerAsync("third", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        await PrepareReadyChannelAsync(third, function);
        ScriptInvocationContext firstActive = CreateInvocation(function);
        ScriptInvocationContext thirdActive = CreateInvocation(function);
        Assert.True(first.Channel.TryPostInvocation(firstActive));
        Assert.True(third.Channel.TryPostInvocation(thirdActive));
        await ReadInvocationAsync(first);
        await ReadInvocationAsync(third);
        PublishReady(first.Channel, second.Channel, third.Channel);
        List<string> attempts = [];
        Mock<IWorkerInvocationTracker> tracker = new();
        tracker.Setup(value => value.TryDispatchInvocation(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>()))
            .Returns((string invocationId, string workerId, Func<bool> post) =>
            {
                attempts.Add(workerId);
                return post();
            });
        using RpcClientFunctionInvocationDispatcher dispatcher = new(
            _registry.Object, NullLogger<RpcClientFunctionInvocationDispatcher>.Instance, tracker.Object);
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext secondActive = CreateInvocation(function);

        await dispatcher.InvokeAsync(secondActive);
        Assert.Equal(["first", "second"], attempts);
        await ReadInvocationAsync(second);
        await CompleteInvocationAsync(third, thirdActive);
        attempts.Clear();
        ScriptInvocationContext next = CreateInvocation(function);

        await dispatcher.InvokeAsync(next);

        Assert.Equal(["second", "first", "third"], attempts);
        Assert.Equal(next.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(third)).InvocationId);
    }

    [Fact]
    public async Task InvokeAsync_SkipsFullWorkersAndAdvancesFromSelectedWorker()
    {
        await using ClientWorkerChannelTestHarness first = await CreateLimitedWorkerAsync("first", 1);
        await using ClientWorkerChannelTestHarness second = await CreateLimitedWorkerAsync("second", 2);
        await using ClientWorkerChannelTestHarness third = await CreateLimitedWorkerAsync("third", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        await PrepareReadyChannelAsync(third, function);
        PublishReady(first.Channel, second.Channel, third.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        ScriptInvocationContext[] invocations = [.. Enumerable.Range(0, 4).Select(_ => CreateInvocation(function))];
        foreach (ScriptInvocationContext invocation in invocations)
        {
            await dispatcher.InvokeAsync(invocation);
        }

        Assert.Equal(invocations[0].ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(first)).InvocationId);
        Assert.Equal(invocations[1].ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(second)).InvocationId);
        Assert.Equal(invocations[2].ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(third)).InvocationId);
        Assert.Equal(invocations[3].ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(second)).InvocationId);

        Task waiting = dispatcher.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(third, invocations[2]);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(third);
        Assert.False(first.Transport.Requests.TryPeek(out _));
        Assert.False(second.Transport.Requests.TryPeek(out _));
    }

    [Fact]
    public async Task TryPostInvocation_ParallelAdmissionSharesLimitAcrossFunctions()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 3);
        FunctionMetadata first = CreateHttpGroupFunction();
        FunctionMetadata second = CreateHttpGroupFunction();
        second.Name = "SecondFunction";
        worker.Channel.SetupFunctionInvocationBuffers([first, second]);
        WorkerChannel channel = worker.Channel;

        Task<bool>[] attempts = [.. Enumerable.Range(0, 100).Select(i => Task.Run(
            () => channel.TryPostInvocation(CreateInvocation(i % 2 == 0 ? first : second))))];
        bool[] accepted = await Task.WhenAll(attempts);

        Assert.Equal(3, accepted.Count(value => value));
        Assert.Equal(3, worker.Channel.FunctionInputBuffers.Values.Sum(buffer => buffer.Count));
        worker.Channel.Shutdown(new InvalidOperationException("Test shutdown"));
        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task GetCapacityAvailableTask_SharesWaitWithoutReservingCapacity()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        ScriptInvocationContext first = CreateInvocation(function);
        await worker.Channel.GetCapacityAvailableTask().WaitAsync(TestTimeout);
        Assert.True(worker.Channel.TryPostInvocation(first));
        await ReadInvocationAsync(worker);
        Task available = worker.Channel.GetCapacityAvailableTask();
        Assert.False(available.IsCompleted);
        Assert.Same(available, worker.Channel.GetCapacityAvailableTask());

        await CompleteInvocationAsync(worker, first);
        await available.WaitAsync(TestTimeout);
        await worker.Channel.GetCapacityAvailableTask().WaitAsync(TestTimeout);

        ScriptInvocationContext second = CreateInvocation(function);
        Assert.True(worker.Channel.TryPostInvocation(second));
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(function)));
        Task nextAvailable = worker.Channel.GetCapacityAvailableTask();
        Assert.NotSame(available, nextAvailable);
        Assert.False(nextAvailable.IsCompleted);
        worker.Channel.Shutdown(new InvalidOperationException("Worker terminated"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.ResultSource.Task.WaitAsync(TestTimeout));
        await nextAvailable.WaitAsync(TestTimeout);
        Assert.True(worker.Channel.IsTerminated);
        Assert.False(worker.Channel.IsReadyForInvocations);
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(function)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryPostInvocation_RejectedPostReturnsConcurrencyLease(bool missingFunction)
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        ScriptInvocationContext rejected = CreateInvocation(missingFunction ? CreateFunction() : function);
        if (missingFunction)
        {
            Assert.Throws<InvalidOperationException>(() => worker.Channel.TryPostInvocation(rejected));
        }
        else
        {
            worker.Channel.FunctionInputBuffers[function.GetFunctionId()].Complete();
            Assert.False(worker.Channel.TryPostInvocation(rejected));
        }

        await worker.Channel.GetCapacityAvailableTask().WaitAsync(TestTimeout);
        FunctionMetadata next = CreateHttpGroupFunction();
        next.Name = "NextFunction";
        worker.Channel.SetupFunctionInvocationBuffers([next]);
        Assert.True(worker.Channel.TryPostInvocation(CreateInvocation(next)));
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(next)));
    }

    [Fact]
    public async Task GetCapacityAvailableTask_DisposalSettlesSharedWait()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        Assert.True(worker.Channel.TryPostInvocation(CreateInvocation(function)));
        Task available = worker.Channel.GetCapacityAvailableTask();
        Assert.False(available.IsCompleted);

        await worker.Channel.DisposeAsync();

        await available.WaitAsync(TestTimeout);
        Assert.False(worker.Channel.IsReadyForInvocations);
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(function)));
    }

    [Fact]
    public async Task GetCapacityAvailableTask_GracefulShutdownSettlesSharedWait()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        ScriptInvocationContext active = CreateInvocation(function);
        Assert.True(worker.Channel.TryPostInvocation(active));
        await ReadInvocationAsync(worker);
        Task available = worker.Channel.GetCapacityAvailableTask();
        Assert.False(available.IsCompleted);

        worker.Channel.Shutdown(null);

        await available.WaitAsync(TestTimeout);
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => active.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.IsNotType<TimeoutException>(exception);
        Assert.False(worker.Channel.IsReadyForInvocations);
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(function)));
    }

    [Fact]
    public async Task TryPostInvocation_DuplicateContextDoesNotAcquireAnotherLease()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 2);
        FunctionMetadata function = CreateHttpGroupFunction();
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        Assert.True(worker.Channel.TryPostInvocation(active));

        Assert.Throws<InvalidOperationException>(() => worker.Channel.TryPostInvocation(active));

        Assert.True(worker.Channel.TryPostInvocation(CreateInvocation(function)));
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(function)));
        Assert.Equal(2, worker.Channel.FunctionInputBuffers[function.GetFunctionId()].Count);
    }

    [Fact]
    public async Task GetCapacityAvailableTask_CancelingObserverPreservesSharedWait()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        ScriptInvocationContext active = CreateInvocation(function);
        Assert.True(worker.Channel.TryPostInvocation(active));
        await ReadInvocationAsync(worker);
        Task available = worker.Channel.GetCapacityAvailableTask();
        using CancellationTokenSource cancellation = new();
        Task observer = available.WaitAsync(cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer);
        Assert.False(available.IsCompleted);
        Assert.Same(available, worker.Channel.GetCapacityAvailableTask());
        Assert.False(worker.Channel.TryPostInvocation(CreateInvocation(function)));

        await CompleteInvocationAsync(worker, active);
        await available.WaitAsync(TestTimeout);
        Assert.True(worker.Channel.TryPostInvocation(CreateInvocation(function)));
    }

    [Fact]
    public async Task InvokeAsync_ParallelDispatchersCannotExceedWorkerLimit()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 3);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher first = CreateDispatcher();
        using RpcClientFunctionInvocationDispatcher second = CreateDispatcher();
        await first.InitializeAsync([function]);
        await second.InitializeAsync([function]);
        using CancellationTokenSource cancellation = new();
        ScriptInvocationContext[] contexts = [.. Enumerable.Range(0, 50).Select(_ => CreateInvocation(function, cancellation.Token))];
        Task[] dispatches = new Task[contexts.Length];
        await Task.WhenAll(Enumerable.Range(0, contexts.Length).Select(i => Task.Run(() =>
        {
            dispatches[i] = (i % 2 == 0 ? first : second).InvokeAsync(contexts[i]);
        })));

        for (int i = 0; i < 3; i++)
        {
            string invocationId = (await ReadInvocationAsync(worker)).InvocationId;
            int index = Array.FindIndex(contexts, context =>
                string.Equals(context.ExecutionContext.InvocationId.ToString(), invocationId, StringComparison.Ordinal));
            await dispatches[index].WaitAsync(TestTimeout);
        }

        Assert.Equal(3, dispatches.Count(task => task.IsCompletedSuccessfully));
        Assert.False(worker.Transport.Requests.TryPeek(out _));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(dispatches).WaitAsync(TestTimeout));
        foreach (ScriptInvocationContext context in contexts.Where((_, i) => dispatches[i].IsCompletedSuccessfully))
        {
            await CompleteInvocationAsync(worker, context);
        }

        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_InputConversionFailureReleasesSlot()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext first = CreateInvocation(function);
        first.Inputs = [("input", DataType.String, new ThrowingInput())];
        await dispatcher.InvokeAsync(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ResultSource.Task.WaitAsync(TestTimeout));

        ScriptInvocationContext second = CreateInvocation(function);
        await dispatcher.InvokeAsync(second).WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, second);
        Assert.NotNull(await second.ResultSource.Task.WaitAsync(TestTimeout));
        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task InvokeAsync_LimitSurvivesDispatcherReplacement()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher original = CreateDispatcher();
        using RpcClientFunctionInvocationDispatcher replacement = CreateDispatcher();
        await original.InitializeAsync([function]);
        await replacement.InitializeAsync([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        await original.InvokeAsync(active);
        await ReadInvocationAsync(worker);
        original.Dispose();

        Task waiting = replacement.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(worker, active);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
    }

    [Fact]
    public async Task InvokeAsync_CapacityWaitDoesNotUseReadinessTimeout()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(30));
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(worker);

        Task waiting = dispatcher.InvokeAsync(CreateInvocation(function));
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(worker, active);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_CanceledCapacityWaitDoesNotReleaseAnotherInvocation(bool stopDispatcher)
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        using RpcClientFunctionInvocationDispatcher replacement = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        await replacement.InitializeAsync([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(worker);
        using CancellationTokenSource cancellation = new();
        Task canceled = dispatcher.InvokeAsync(CreateInvocation(function, cancellation.Token));
        Assert.False(canceled.IsCompleted);

        if (stopDispatcher)
        {
            dispatcher.PreShutdown();
        }
        else
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TestTimeout));
        Task waiting = replacement.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
        await CompleteInvocationAsync(worker, active);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
    }

    [Fact]
    public async Task InvokeAsync_CancelingActiveInvocationDoesNotReleaseCapacity()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using CancellationTokenSource cancellation = new();
        ScriptInvocationContext active = CreateInvocation(function, cancellation.Token);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(worker);

        cancellation.Cancel();
        Task waiting = dispatcher.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(worker, active);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
    }

    [Fact]
    public async Task InvokeAsync_NewReadyWorkerWakesCapacityWait()
    {
        await using ClientWorkerChannelTestHarness first = await CreateLimitedWorkerAsync("first", 1);
        await using ClientWorkerChannelTestHarness second = await CreateLimitedWorkerAsync("second", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        PublishReady(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        await dispatcher.InvokeAsync(CreateInvocation(function));
        await ReadInvocationAsync(first);
        Task waiting = dispatcher.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);

        PublishReady(second.Channel);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(second);
        Assert.False(first.Transport.Requests.TryPeek(out _));
    }

    [Fact]
    public async Task InvokeAsync_FunctionLoadFailureReleasesAcceptedCapacity()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        worker.Channel.SendFunctionLoadRequests(new ManagedDependencyOptions(), null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext first = CreateInvocation(function);
        ScriptInvocationContext second = CreateInvocation(function);
        await dispatcher.InvokeAsync(first);
        Task waiting = dispatcher.InvokeAsync(second);
        Assert.False(waiting.IsCompleted);

        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId(), succeeded: false);
        await Assert.ThrowsAnyAsync<Exception>(() => first.ResultSource.Task.WaitAsync(TestTimeout));
        await waiting.WaitAsync(TestTimeout);
        await Assert.ThrowsAnyAsync<Exception>(() => second.ResultSource.Task.WaitAsync(TestTimeout));
        await worker.Channel.DrainInvocationsAsync().WaitAsync(TestTimeout);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
    }

    [Fact]
    public async Task InvokeAsync_TransportTerminationWakesWaitAndDoesNotReuseWorker()
    {
        await using ClientWorkerChannelTestHarness first = await CreateLimitedWorkerAsync("first", 1);
        await using ClientWorkerChannelTestHarness second = await CreateLimitedWorkerAsync("second", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        PublishReady(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(first);
        Task waiting = dispatcher.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);

        first.Transport.CompleteResponses();
        await Assert.ThrowsAnyAsync<Exception>(() => active.ResultSource.Task.WaitAsync(TestTimeout));
        PublishReady(second.Channel);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(second);
        Assert.False(first.Transport.Requests.TryPeek(out _));
    }

    private static Task<ClientWorkerChannelTestHarness> CreateLimitedWorkerAsync(string id, int limit)
        => ClientWorkerChannelTestHarness.CreateAsync(id, functionGroupName: "http",
            maxHttpConcurrency: limit.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static FunctionMetadata CreateHttpGroupFunction()
    {
        FunctionMetadata function = new() { Name = "TestWebhook", Language = "external" };
        function.Bindings.Add(new() { Name = "event", Type = "eventGridTrigger", Direction = BindingDirection.In });
        return function;
    }

    private static async Task<InvocationRequest> ReadInvocationAsync(ClientWorkerChannelTestHarness worker)
        => (await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest)).InvocationRequest;

    private static Task CompleteInvocationAsync(ClientWorkerChannelTestHarness worker, ScriptInvocationContext invocation)
        => worker.Channel.InvokeResponse(new()
        {
            InvocationId = invocation.ExecutionContext.InvocationId.ToString(),
            Result = new() { Status = StatusResult.Types.Status.Success },
        });

    private sealed class ThrowingInput
    {
        public string Value => throw new InvalidOperationException("Input conversion failed");

        public override string ToString() => throw new InvalidOperationException("Input conversion failed");
    }
}
