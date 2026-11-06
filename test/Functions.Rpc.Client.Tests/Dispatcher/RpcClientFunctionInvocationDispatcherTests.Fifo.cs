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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class RpcClientFunctionInvocationDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_HttpAdmissionIsFifoIncludingNewArrivals()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(worker);
        ScriptInvocationContext[] queued = [.. Enumerable.Range(0, 5).Select(_ => CreateInvocation(function))];
        Task[] dispatches = [.. queued.Select(dispatcher.InvokeAsync)];
        Assert.All(dispatches, task => Assert.False(task.IsCompleted));

        await CompleteInvocationAsync(worker, active);
        ScriptInvocationContext newcomer = CreateInvocation(function);
        Task newcomerDispatch = dispatcher.InvokeAsync(newcomer);

        for (int i = 0; i < queued.Length; i++)
        {
            await dispatches[i].WaitAsync(TestTimeout);
            Assert.Equal(queued[i].ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
            Assert.False(newcomerDispatch.IsCompleted);
            await CompleteInvocationAsync(worker, queued[i]);
        }

        await newcomerDispatch.WaitAsync(TestTimeout);
        Assert.Equal(newcomer.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
        await CompleteInvocationAsync(worker, newcomer);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_CanceledHttpWaiterDoesNotBlockNextTurn(bool cancelHead)
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext active = CreateInvocation(function);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(worker);
        using CancellationTokenSource cancellation = new();
        ScriptInvocationContext first = CreateInvocation(function, cancelHead ? cancellation.Token : default);
        ScriptInvocationContext second = CreateInvocation(function, cancelHead ? default : cancellation.Token);
        Task firstDispatch = dispatcher.InvokeAsync(first);
        Task secondDispatch = dispatcher.InvokeAsync(second);
        ScriptInvocationContext last = CreateInvocation(function);
        Task lastDispatch = dispatcher.InvokeAsync(last);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => (cancelHead ? firstDispatch : secondDispatch).WaitAsync(TestTimeout));
        Assert.False(worker.Transport.Requests.TryPeek(out _));
        await CompleteInvocationAsync(worker, active);
        await (cancelHead ? secondDispatch : firstDispatch).WaitAsync(TestTimeout);
        ScriptInvocationContext next = cancelHead ? second : first;
        Assert.Equal(next.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
        Assert.False(lastDispatch.IsCompleted);
        await CompleteInvocationAsync(worker, next);
        await lastDispatch.WaitAsync(TestTimeout);
        Assert.Equal(last.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
        await CompleteInvocationAsync(worker, last);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
    }

    [Fact]
    public async Task InvokeAsync_CancellationRacingTurnGrantDoesNotLeakAdmission()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        for (int i = 0; i < 10; i++)
        {
            ScriptInvocationContext active = CreateInvocation(function);
            await dispatcher.InvokeAsync(active);
            await ReadInvocationAsync(worker);
            ScriptInvocationContext head = CreateInvocation(function);
            Task headDispatch = dispatcher.InvokeAsync(head);
            using CancellationTokenSource cancellation = new();
            Task canceled = dispatcher.InvokeAsync(CreateInvocation(function, cancellation.Token));
            ScriptInvocationContext last = CreateInvocation(function);
            Task lastDispatch = dispatcher.InvokeAsync(last);

            await Task.WhenAll(
                Task.Run(() => CompleteInvocationAsync(worker, active)),
                Task.Run(cancellation.Cancel)).WaitAsync(TestTimeout);

            await headDispatch.WaitAsync(TestTimeout);
            Assert.Equal(head.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TestTimeout));
            Assert.False(lastDispatch.IsCompleted);
            await CompleteInvocationAsync(worker, head);
            await lastDispatch.WaitAsync(TestTimeout);
            Assert.Equal(last.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
            await CompleteInvocationAsync(worker, last);
            Assert.False(worker.Transport.Requests.TryPeek(out _));
        }
    }

    [Fact]
    public async Task InvokeAsync_CancellationAfterTurnGrantDisposesUntransferredLease()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 1);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using CancellationTokenSource cancellation = new();
        AsyncLocal<bool> canceledCaller = new();
        int samples = 0;
        _registry.Setup(registry => registry.GetReadyChannelsAsync()).Returns(() =>
        {
            // This caller samples once while queued, then again after its turn has been granted.
            if (canceledCaller.Value && Interlocked.Increment(ref samples) == 2)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            return new ValueTask<IReadOnlyList<WorkerChannel>>(_readyChannels.ToArray());
        });
        ScriptInvocationContext active = CreateInvocation(function);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(worker);
        ScriptInvocationContext head = CreateInvocation(function);
        Task headDispatch = dispatcher.InvokeAsync(head);
        canceledCaller.Value = true;
        Task canceled = dispatcher.InvokeAsync(CreateInvocation(function, cancellation.Token));
        canceledCaller.Value = false;
        ScriptInvocationContext last = CreateInvocation(function);
        Task lastDispatch = dispatcher.InvokeAsync(last);

        await CompleteInvocationAsync(worker, active);
        await headDispatch.WaitAsync(TestTimeout);
        Assert.Equal(head.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TestTimeout));
        Assert.Equal(2, samples);
        Assert.False(lastDispatch.IsCompleted);

        await CompleteInvocationAsync(worker, head);
        await lastDispatch.WaitAsync(TestTimeout);
        Assert.Equal(last.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(worker)).InvocationId);
        await CompleteInvocationAsync(worker, last);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
    }

    [Fact]
    public async Task InvokeAsync_HttpFifoDoesNotSerializeExecution()
    {
        await using ClientWorkerChannelTestHarness worker = await CreateLimitedWorkerAsync("worker", 2);
        FunctionMetadata function = CreateHttpGroupFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext first = CreateInvocation(function);
        ScriptInvocationContext second = CreateInvocation(function);
        ScriptInvocationContext third = CreateInvocation(function);

        await dispatcher.InvokeAsync(first).WaitAsync(TestTimeout);
        await dispatcher.InvokeAsync(second).WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        await ReadInvocationAsync(worker);
        Task waiting = dispatcher.InvokeAsync(third);
        Assert.False(waiting.IsCompleted);
        Assert.False(first.ResultSource.Task.IsCompleted);
        Assert.False(second.ResultSource.Task.IsCompleted);

        await CompleteInvocationAsync(worker, first);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        Assert.False(second.ResultSource.Task.IsCompleted);
        await CompleteInvocationAsync(worker, second);
        await CompleteInvocationAsync(worker, third);
    }

    [Fact]
    public async Task InvokeAsync_BusyHttpQueueDoesNotUseReadinessTimeout()
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
        ScriptInvocationContext first = CreateInvocation(function);
        ScriptInvocationContext second = CreateInvocation(function);
        Task firstDispatch = dispatcher.InvokeAsync(first);
        Task secondDispatch = dispatcher.InvokeAsync(second);

        await Task.Delay(100);
        Assert.False(firstDispatch.IsCompleted);
        Assert.False(secondDispatch.IsCompleted);
        await CompleteInvocationAsync(worker, active);
        await firstDispatch.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, first);
        await secondDispatch.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        await CompleteInvocationAsync(worker, second);
    }

    [Fact]
    public async Task InvokeAsync_FullHttpQueueDoesNotBlockAnotherFunctionGroup()
    {
        await using ClientWorkerChannelTestHarness http = await CreateLimitedWorkerAsync("http-worker", 1);
        FunctionMetadata httpFunction = CreateHttpGroupFunction();
        FunctionMetadata background = new() { Name = "QueueFunction", Language = "external" };
        background.Bindings.Add(new() { Name = "item", Type = "queueTrigger", Direction = BindingDirection.In });
        await using ClientWorkerChannelTestHarness queue = await ClientWorkerChannelTestHarness.CreateAsync(
            "queue-worker", functionGroupName: background.GetFunctionGroup());
        await PrepareReadyChannelAsync(http, httpFunction);
        await PrepareReadyChannelAsync(queue, background);
        PublishReady(http.Channel, queue.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([httpFunction, background]);
        ScriptInvocationContext active = CreateInvocation(httpFunction);
        await dispatcher.InvokeAsync(active);
        await ReadInvocationAsync(http);
        ScriptInvocationContext waitingHttp = CreateInvocation(httpFunction);
        Task waiting = dispatcher.InvokeAsync(waitingHttp);
        Assert.False(waiting.IsCompleted);

        ScriptInvocationContext otherGroup = CreateInvocation(background);
        await dispatcher.InvokeAsync(otherGroup).WaitAsync(TestTimeout);
        Assert.Equal(otherGroup.ExecutionContext.InvocationId.ToString(), (await ReadInvocationAsync(queue)).InvocationId);
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(queue, otherGroup);
        await CompleteInvocationAsync(http, active);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(http);
        await CompleteInvocationAsync(http, waitingHttp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_QueuedHttpRequestKeepsItsOwnMissingWorkerDeadline(bool wrongGroupReady)
    {
        await using ClientWorkerChannelTestHarness other = await ClientWorkerChannelTestHarness.CreateAsync(
            "queue-worker", functionGroupName: FunctionGroups.ForFunction("QueueFunction"));
        FunctionMetadata background = new() { Name = "QueueFunction", Language = "external" };
        background.Bindings.Add(new() { Name = "item", Type = "queueTrigger", Direction = BindingDirection.In });
        await PrepareReadyChannelAsync(other, background);
        if (wrongGroupReady)
        {
            PublishReady(other.Channel);
        }

        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromSeconds(2));
        FunctionMetadata function = CreateHttpGroupFunction();
        await dispatcher.InitializeAsync([function]);
        Task first = dispatcher.InvokeAsync(CreateInvocation(function));
        Task second = dispatcher.InvokeAsync(CreateInvocation(function));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => second.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Contains("http", exception.Message, StringComparison.OrdinalIgnoreCase);
        TimeoutException firstException = await Assert.ThrowsAsync<TimeoutException>(() => first.WaitAsync(TestTimeout));
        Assert.Contains("http", firstException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_StoppingDispatcherCancelsAllHttpWaitersWithoutReleasingActiveWork(bool dispose)
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
        Task first = original.InvokeAsync(CreateInvocation(function));
        Task second = original.InvokeAsync(CreateInvocation(function));

        if (dispose)
        {
            original.Dispose();
        }
        else
        {
            original.PreShutdown();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestTimeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TestTimeout));
        Task waiting = replacement.InvokeAsync(CreateInvocation(function));
        Assert.False(waiting.IsCompleted);
        await CompleteInvocationAsync(worker, active);
        await waiting.WaitAsync(TestTimeout);
        await ReadInvocationAsync(worker);
        Assert.False(worker.Transport.Requests.TryPeek(out _));
    }
}
