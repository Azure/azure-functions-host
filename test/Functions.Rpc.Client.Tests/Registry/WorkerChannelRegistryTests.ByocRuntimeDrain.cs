// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Logging.Abstractions;
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
        IByocRuntimeDrainManager manager = new ByocRuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<ByocRuntimeDrainManager>.Instance);

        manager.Accept(ByocDrainModel.Immediate);
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

        IByocRuntimeDrainManager manager = new ByocRuntimeDrainManager(
            registry,
            TimeProvider.System,
            NullLogger<ByocRuntimeDrainManager>.Instance);

        ByocRuntimeDrainReceipt receipt = manager.Accept(
            ByocDrainModel.Immediate);

        Assert.Equal(ByocDrainModel.Immediate, receipt.DrainModel);
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
