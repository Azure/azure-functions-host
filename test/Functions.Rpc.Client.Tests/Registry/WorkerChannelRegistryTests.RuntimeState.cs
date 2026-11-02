// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    [Fact]
    public async Task GetChangeTask_ObservesMembershipCancellationAndDisposal()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl second = new("second");
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task change = registry.GetChangeTask();
        Assert.Same(change, registry.GetChangeTask());
        using CancellationTokenSource canceled = new();
        Task canceledWait = change.WaitAsync(canceled.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        Assert.False(change.IsCompleted);

        await LinkAsync(registry, first.Id);
        await change.WaitAsync(TestTimeout);
        Assert.Single(registry.GetInitializedChannels());
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);

        change = registry.GetChangeTask();
        Assert.False(change.IsCompleted);
        await LinkAsync(registry, second.Id);
        await change.WaitAsync(TestTimeout);
        Assert.Equal(2, registry.GetInitializedChannels().Count);

        change = registry.GetChangeTask();
        Assert.True(await registry.UnlinkAsync(first.Id));
        await change.WaitAsync(TestTimeout);

        change = registry.GetChangeTask();
        second.Complete();
        await change.WaitAsync(TestTimeout);
        Assert.Empty(registry.GetInitializedChannels());

        change = registry.GetChangeTask();
        await registry.DisposeAsync();
        await change.WaitAsync(TestTimeout);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.GetChangeTask());
    }

    [Fact]
    public async Task GetChangeTask_ObservesFailedSetupContextReplacementAndLaterReadiness()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first", setupFailureCount: 1);
        ChannelControl second = new("second");
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);
        Task change = registry.GetChangeTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.ActivateAsync([CreateFunction("First")], new(), functionTimeout: null));
        await change.WaitAsync(TestTimeout);
        Assert.True(first.Channel.IsChannelReadyForInvocations());
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);

        change = registry.GetChangeTask();
        await registry.ActivateAsync([CreateFunction("Second")], new(), functionTimeout: null);
        await change.WaitAsync(TestTimeout);
        Assert.Single(await registry.GetReadyChannelsAsync());

        TaskCompletionSource<Task> laterSetupChange = new(TaskCreationOptions.RunContinuationsAsynchronously);
        second.MetricsLogger.Setup(logger => logger.BeginEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string eventName, string _, string _) =>
            {
                if (string.Equals(eventName, MetricEventNames.FunctionLoadRequestResponse, StringComparison.Ordinal))
                {
                    // Capture after initialized publication but before setup completes under the same reentrant lock.
                    laterSetupChange.TrySetResult(registry.GetChangeTask());
                }

                return null;
            });
        await LinkAsync(registry, second.Id);
        Task readinessChanged = await laterSetupChange.Task.WaitAsync(TestTimeout);
        await readinessChanged.WaitAsync(TestTimeout);
        Assert.Equal(2, (await registry.GetReadyChannelsAsync()).Count);

        change = registry.GetChangeTask();
        await registry.ActivateAsync([], new(), functionTimeout: null);
        await change.WaitAsync(TestTimeout);
        Assert.Equal(2, registry.GetInitializedChannels().Count);
        Assert.False(registry.GetChangeTask().IsCompleted);
    }

    [Theory]
    [InlineData(10, 10, false)]
    [InlineData(7, 13, false)]
    [InlineData(7, 13, true)]
    public async Task RuntimeState_SumsAssignedLimitsOnlyAfterSuccessfulRegistrySetup(int firstLimit, int secondLimit, bool failLaterSetup)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first", functionGroupName: "http", maxHttpConcurrency: firstLimit);
        ChannelControl background = new("background", functionGroupName: "background");
        ChannelControl later = new("later", blockSetup: true, setupFailureCount: failLaterSetup ? 1 : 0,
            functionGroupName: "HTTP", maxHttpConcurrency: secondLimit);
        harness.Enqueue(first);
        harness.Enqueue(background);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;
        using RuntimeStateManager manager = CreateRuntimeStateManager(registry);
        await manager.StartAsync(CancellationToken.None);
        await LinkAsync(registry, first.Id);
        RuntimeState initialized = await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 1);
        Assert.Equal(0, initialized.HttpCapacity);
        await registry.ActivateAsync([CreateFunction("First")], new(), functionTimeout: null);
        await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == firstLimit);
        await LinkAsync(registry, background.Id);
        await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 2);

        try
        {
            await LinkAsync(registry, later.Id);
            await later.SetupEntered.WaitAsync(TestTimeout);
            Assert.Equal(firstLimit, manager.Current.HttpCapacity);
        }
        finally
        {
            later.AllowSetup();
        }

        int expected = failLaterSetup ? firstLimit : firstLimit + secondLimit;
        RuntimeState current = await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 3 && state.HttpCapacity == expected);
        Assert.Equal(3, current.WorkerCount);
        if (failLaterSetup)
        {
            Assert.True(later.Channel.IsChannelReadyForInvocations());
            Assert.DoesNotContain(later.Channel, await registry.GetReadyChannelsAsync());
            await registry.ActivateAsync([CreateFunction("Replacement")], new(), functionTimeout: null);
            await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == firstLimit + secondLimit);
        }

        await registry.UnlinkAsync(first.Id);
        await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 2 && state.HttpCapacity == secondLimit);
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeState_PriorReadyContextCannotMaskReplacementSetupFailure()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", functionGroupName: "http", maxHttpConcurrency: 10);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        using RuntimeStateManager manager = CreateRuntimeStateManager(registry);
        await manager.StartAsync(CancellationToken.None);
        await LinkAsync(registry, channel.Id);
        await registry.ActivateAsync([CreateFunction("First")], new(), functionTimeout: null);
        await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == 10);
        channel.MetricsLogger.Setup(logger => logger.BeginEvent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("Replacement load setup failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.ActivateAsync([CreateFunction("Replacement")], new(), functionTimeout: null));

        RuntimeState state = await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == 0);
        Assert.Equal(1, state.WorkerCount);
        Assert.True(channel.Channel.IsChannelReadyForInvocations());
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);
        await manager.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeState_TerminalChannelsWithdrawCapacity(bool functionLoadFailure)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", functionGroupName: "http", maxHttpConcurrency: 10);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        using RuntimeStateManager manager = CreateRuntimeStateManager(registry);
        await manager.StartAsync(CancellationToken.None);
        await LinkAsync(registry, channel.Id);
        await registry.ActivateAsync([CreateFunction("Function")], new(), functionTimeout: null);
        await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == 10);
        Task changed = registry.GetChangeTask();

        if (functionLoadFailure)
        {
            await channel.FailFunctionLoadAsync();
        }
        else
        {
            channel.Complete(new InvalidOperationException("Transport failed."));
        }

        await changed.WaitAsync(TestTimeout);
        RuntimeState state = await WaitForRuntimeStateAsync(manager, state => state.WorkerCount == 0);
        Assert.Equal(0, state.HttpCapacity);
        await manager.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RuntimeState_TerminalCapacityIsWithdrawnWhileLaterSetupIsBlocked(
        bool functionLoadFailure, bool failLaterSetup, bool keepHealthyWorker)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first", functionGroupName: "http", maxHttpConcurrency: 10);
        ChannelControl healthy = new("healthy", functionGroupName: "http", maxHttpConcurrency: 7);
        ChannelControl later = new("later", blockSetup: true, setupFailureCount: failLaterSetup ? 1 : 0,
            functionGroupName: "http", maxHttpConcurrency: 13);
        harness.Enqueue(first);
        harness.Enqueue(healthy);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;
        using RuntimeStateManager manager = CreateRuntimeStateManager(registry);
        await manager.StartAsync(CancellationToken.None);
        await LinkAsync(registry, first.Id);
        if (keepHealthyWorker)
        {
            await LinkAsync(registry, healthy.Id);
        }

        await registry.ActivateAsync([CreateFunction("Function")], new(), functionTimeout: null);
        int healthyCapacity = keepHealthyWorker ? 7 : 0;
        await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == 10 + healthyCapacity);
        IReadOnlyList<WorkerChannel> beforeLaterLink = registry.GetInitializedChannels();

        try
        {
            await LinkAsync(registry, later.Id);
            await later.SetupEntered.WaitAsync(TestTimeout);

            var observation = await Task.Run(() =>
            {
                Task change = registry.GetChangeTask();
                IReadOnlyList<WorkerChannel> channels = registry.GetInitializedChannels();
                return (Change: change, Channels: channels);
            }).WaitAsync(TestTimeout);
            Assert.False(observation.Change.IsCompleted);
            Assert.Equal(keepHealthyWorker ? 3 : 2, observation.Channels.Count);
            Assert.Equal(keepHealthyWorker ? 2 : 1, beforeLaterLink.Count);

            if (functionLoadFailure)
            {
                await first.FailFunctionLoadAsync();
            }
            else
            {
                first.Complete(new InvalidOperationException("Transport failed during another worker's setup."));
            }

            RuntimeState withdrawn = await WaitForRuntimeStateAsync(manager, state => state.HttpCapacity == healthyCapacity);
            Assert.Equal(observation.Channels.Count, withdrawn.WorkerCount);
            Assert.Equal(1, later.SetupAttempts);
            Assert.True(first.Channel.IsTerminated);
            Assert.False(observation.Change.IsCompleted);
        }
        finally
        {
            later.AllowSetup();
        }

        int finalCapacity = healthyCapacity + (failLaterSetup ? 0 : 13);
        await WaitForRuntimeStateAsync(manager,
            state => state.WorkerCount == (keepHealthyWorker ? 2 : 1) && state.HttpCapacity == finalCapacity);
        await manager.StopAsync(CancellationToken.None);
    }

    private static RuntimeStateManager CreateRuntimeStateManager(IWorkerChannelRegistry registry)
        => new(registry, Mock.Of<IHostApplicationLifetime>(), TimeProvider.System, NullLogger<RuntimeStateManager>.Instance);

    private static async Task<RuntimeState> WaitForRuntimeStateAsync(RuntimeStateManager manager, Func<RuntimeState, bool> predicate)
    {
        using CancellationTokenSource timeout = new(TestTimeout);
        while (true)
        {
            Task changed = manager.GetChangeTask();
            RuntimeState state = manager.Current;
            if (predicate(state))
            {
                return state;
            }

            await changed.WaitAsync(timeout.Token);
        }
    }
}
