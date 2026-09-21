// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Tests client-backed worker channel registry lifecycle.
/// </summary>
public sealed class WorkerChannelRegistryTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task LinkAsync_ConcurrentAndCompletedReplayShareInitialization()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;

        Task<WorkerChannel> first = linker.LinkAsync("worker", CreateEndpoint("worker"));
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerChannel> replay = linker.LinkAsync("worker", CreateEndpoint("worker"));
        Assert.False(first.IsCompleted);
        Assert.False(replay.IsCompleted);
        channel.AllowStart();
        WorkerChannel linked = await first.WaitAsync(TestTimeout);

        Assert.Same(linked, await replay.WaitAsync(TestTimeout));
        Assert.Same(linked, await linker.LinkAsync("worker", CreateEndpoint("worker")));
        Assert.Single(harness.Transports);
        Assert.Single(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task LinkAsync_ConflictingEndpointIsRejectedWhilePendingAndReady()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        Task<WorkerChannel> first = linker.LinkAsync("worker", CreateEndpoint("worker"));
        await channel.StartEntered.WaitAsync(TestTimeout);

        foreach (bool ready in new[] { false, true })
        {
            if (ready)
            {
                channel.AllowStart();
                await first.WaitAsync(TestTimeout);
            }

            WorkerLinkException conflict = await Assert.ThrowsAsync<WorkerLinkException>(
                () => linker.LinkAsync("worker", CreateEndpoint("different")));
            Assert.Equal(WorkerLinkFailureReason.Conflict, conflict.Reason);
        }

        Assert.Single(harness.Transports);
    }

    [Fact]
    public async Task LinkAsync_FailedHandshakeCleansBeforeRetry()
    {
        RegistryHarness harness = new();
        ChannelControl failed = new("worker", blockStart: true);
        ChannelControl replacement = new("worker");
        harness.Enqueue(failed);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        TimeoutException failure = new("WorkerInit timed out.");
        Task<WorkerChannel> link = linker.LinkAsync("worker", CreateEndpoint("worker"));
        await failed.StartEntered.WaitAsync(TestTimeout);
        failed.FailStart(failure);

        TimeoutException actual = await Assert.ThrowsAsync<TimeoutException>(() => link.WaitAsync(TestTimeout));
        Assert.Same(failure, actual);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Equal(1, failed.DisposeCount);
        WorkerChannel linked = await linker.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, linked);
        Assert.Single(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task LinkAsync_CleanCloseDuringHandshakeThrowsIOException()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> link = registry.LinkAsync("worker", CreateEndpoint("worker"));
        await channel.StartEntered.WaitAsync(TestTimeout);
        channel.Complete();

        IOException failure = await Assert.ThrowsAsync<IOException>(() => link.WaitAsync(TestTimeout));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task LinkAsync_CancelingReplayDoesNotCancelOriginal()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        using CancellationTokenSource cancellation = new();
        Task<WorkerChannel> first = linker.LinkAsync("worker", CreateEndpoint("worker"));
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerChannel> replay = linker.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replay);
        Assert.False(first.IsCompleted);
        channel.AllowStart();
        Assert.Same(channel.Channel, await first.WaitAsync(TestTimeout));
        Assert.Equal(0, channel.DisposeCount);
    }

    [Fact]
    public async Task LinkAsync_CancelingOriginalCleansBeforeRetry()
    {
        RegistryHarness harness = new();
        ChannelControl canceled = new("worker", blockStart: true);
        ChannelControl replacement = new("worker");
        harness.Enqueue(canceled);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        using CancellationTokenSource cancellation = new();
        Task<WorkerChannel> first = linker.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token);
        await canceled.StartEntered.WaitAsync(TestTimeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestTimeout));
        Assert.Equal(1, canceled.DisposeCount);
        Assert.Empty(registry.GetInitializedChannels());
        WorkerChannel linked = await linker.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, linked);
    }

    [Fact]
    public async Task LinkAsync_TerminalWorkerCannotBeResurrected()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        await linker.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        channel.Complete();
        await channel.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => !registry.TryGetInitializedChannel("worker", out _));

        WorkerLinkException failure = await Assert.ThrowsAsync<WorkerLinkException>(
            () => linker.LinkAsync("worker", CreateEndpoint("worker")));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, failure.Reason);
        Assert.Single(harness.Transports);
    }

    [Fact]
    public async Task LinkAsync_DisposalRejectsNewAndPendingLinks()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        Task<WorkerChannel> link = linker.LinkAsync("worker", CreateEndpoint("worker"));
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task dispose = registry.DisposeAsync().AsTask();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => link.WaitAsync(TestTimeout));
        await dispose.WaitAsync(TestTimeout);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => linker.LinkAsync("other", CreateEndpoint("other")));
        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task LinkAsync_PublishesOnlyAfterInitialization()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;

        Task<WorkerChannel> link = LinkAsync(registry, "worker");
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerChannel> initialized = registry.WaitForFirstInitializedAsync();

        Assert.False(registry.TryGetInitializedChannel("worker", out _));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.False(initialized.IsCompleted);

        channel.AllowStart();
        WorkerChannel linkedChannel = await link.WaitAsync(TestTimeout);

        Assert.Same(channel.Channel, linkedChannel);
        Assert.True(registry.TryGetInitializedChannel("worker", out WorkerChannel foundChannel));
        Assert.Same(channel.Channel, foundChannel);
        Assert.Same(channel.Channel, await initialized.WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData("second", false)]
    [InlineData("FIRST", false)]
    [InlineData("second", true)]
    public async Task LinkAsync_DifferentWorkersInitializeIndependently(string secondWorkerId, bool failFirst)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first", blockStart: true);
        ChannelControl second = new(secondWorkerId, blockStart: true);
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;

        Task<WorkerChannel> firstLink = LinkAsync(registry, "first");
        await first.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerChannel> secondLink = LinkAsync(registry, secondWorkerId);
        await second.StartEntered.WaitAsync(TestTimeout);

        Assert.False(firstLink.IsCompleted);
        Assert.False(secondLink.IsCompleted);
        Assert.Equal(2, harness.Transports.Count);
        second.AllowStart();
        Assert.Same(second.Channel, await secondLink.WaitAsync(TestTimeout));
        Assert.False(firstLink.IsCompleted);
        Assert.Same(second.Channel, Assert.Single(registry.GetInitializedChannels()));

        if (failFirst)
        {
            IOException failure = new("First worker initialization failed.");
            first.FailStart(failure);
            IOException actual = await Assert.ThrowsAsync<IOException>(() => firstLink.WaitAsync(TestTimeout));
            Assert.Same(failure, actual);
            Assert.Same(second.Channel, Assert.Single(registry.GetInitializedChannels()));
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(0, second.DisposeCount);
        }
        else
        {
            first.AllowStart();
            Assert.Same(first.Channel, await firstLink.WaitAsync(TestTimeout));
            Assert.Equal(2, registry.GetInitializedChannels().Count);
            Assert.True(registry.TryGetInitializedChannel("first", out WorkerChannel firstChannel));
            Assert.Same(first.Channel, firstChannel);
            Assert.True(registry.TryGetInitializedChannel(secondWorkerId, out WorkerChannel secondChannel));
            Assert.Same(second.Channel, secondChannel);
        }
    }

    [Fact]
    public async Task LinkAsync_InterfaceAndConcreteCallsSharePendingAttempt()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;

        Task<WorkerChannel> firstLink = LinkAsync(registry, "worker");
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerChannel> replay = ((IWorkerChannelRegistry)registry).LinkAsync("worker", CreateEndpoint("worker"));
        Assert.False(replay.IsCompleted);
        harness.ChannelFactory.Verify(factory => factory.Create("worker", It.IsAny<DuplexChannel<StreamingMessage>>()), Times.Once);
        channel.AllowStart();
        WorkerChannel initialized = await firstLink.WaitAsync(TestTimeout);
        Assert.Same(initialized, await replay.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task UnlinkAsync_CanceledWhileLinkIsPending_DoesNotRemoveSlot()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        using CancellationTokenSource cancellationSource = new();

        Task<WorkerChannel> link = LinkAsync(registry, "worker");
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<bool> unlink = registry.UnlinkAsync("worker", cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unlink.WaitAsync(TestTimeout));
        Task<WorkerChannel> replay = LinkAsync(registry, "worker");
        Assert.False(replay.IsCompleted);

        channel.AllowStart();
        WorkerChannel linkedChannel = await link.WaitAsync(TestTimeout);
        Assert.Same(channel.Channel, linkedChannel);
        Assert.True(registry.TryGetInitializedChannel("worker", out WorkerChannel initializedChannel));
        Assert.Same(channel.Channel, initializedChannel);
        Assert.Same(initializedChannel, await replay.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task UnlinkAsync_BeforeLinkStarts_WaitsWithoutBlockingOtherWorkers()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        ChannelControl other = new("other");
        harness.Enqueue(channel);
        harness.Enqueue(other);
        await using WorkerChannelRegistry registry = harness.Registry;
        QueuedSynchronizationContext context = new();
        Task<WorkerChannel> link = context.Run(() => LinkAsync(registry, "worker"));
        Task<bool> unlink = null;

        try
        {
            Assert.Empty(harness.Transports);
            unlink = registry.UnlinkAsync("worker");
            Assert.False(unlink.IsCompleted);
            Assert.Same(link, LinkAsync(registry, "worker"));
            WorkerChannel otherChannel = await LinkAsync(registry, "other").WaitAsync(TestTimeout);
            Assert.Same(other.Channel, otherChannel);
            Assert.False(link.IsCompleted);
            Assert.False(unlink.IsCompleted);
        }
        finally
        {
            context.RunContinuations();
            channel.AllowStart();
            await link.WaitAsync(TestTimeout);
            if (unlink is not null)
            {
                await unlink.WaitAsync(TestTimeout);
            }
        }

        Assert.True(await unlink);
        Assert.Equal(1, channel.DisposeCount);
        Assert.Equal(0, other.DisposeCount);
        Assert.Equal(2, harness.Transports.Count);
        Assert.Same(other.Channel, Assert.Single(registry.GetInitializedChannels()));
    }

    [Fact]
    public async Task LinkAsync_CanceledBeforeQueuedStart_ReleasesReservationAndAllowsRetry()
    {
        RegistryHarness harness = new();
        ChannelControl replacement = new("worker");
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        using CancellationTokenSource cancellation = new();
        QueuedSynchronizationContext context = new();
        Task<WorkerChannel> link = context.Run(
            () => registry.LinkAsync("worker", CreateEndpoint("worker"), cancellation.Token));
        Task<bool> unlink = registry.UnlinkAsync("worker");

        try
        {
            cancellation.Cancel();
            Assert.False(unlink.IsCompleted);
        }
        finally
        {
            context.RunContinuations();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => link.WaitAsync(TestTimeout));
            await unlink.WaitAsync(TestTimeout);
        }

        Assert.False(await unlink);
        harness.DuplexFactory.Verify(
            factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        WorkerChannel linked = await LinkAsync(registry, "worker").WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, linked);
        Assert.Single(harness.Transports);
    }

    [Fact]
    public async Task LinkAsync_FailedInitializationAllowsSerializedRelink()
    {
        RegistryHarness harness = new();
        ChannelControl failedChannel = new("worker", blockStart: true);
        ChannelControl replacement = new("worker", blockStart: true);
        harness.Enqueue(failedChannel);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        IOException expected = new("initialization failed");

        Task<WorkerChannel> failedLink = LinkAsync(registry, "worker");
        await failedChannel.StartEntered.WaitAsync(TestTimeout);
        failedChannel.FailStart(expected);

        IOException actual = await Assert.ThrowsAsync<IOException>(
            () => failedLink.WaitAsync(TestTimeout));
        Assert.Same(expected, actual);

        Task<WorkerChannel> replacementLink = LinkAsync(registry, "worker");
        await replacement.StartEntered.WaitAsync(TestTimeout);
        replacement.AllowStart();

        Assert.Same(replacement.Channel, await replacementLink.WaitAsync(TestTimeout));
        Assert.Equal(1, failedChannel.DisposeCount);
    }

    [Fact]
    public async Task LinkAsync_ChannelFactoryFailureDisposesUnownedTransport()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        InvalidOperationException expected = new("factory failed");
        harness.ChannelFactory
            .Setup(factory => factory.Create(It.IsAny<string>(), It.IsAny<DuplexChannel<StreamingMessage>>()))
            .Throws(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LinkAsync(registry, "worker").WaitAsync(TestTimeout));

        Assert.Same(expected, actual);
        TestDuplexChannel<StreamingMessage> transport = Assert.Single(harness.Transports);
        await transport.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task LinkAsync_NullChannelFactoryResultDisposesUnownedTransport()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        harness.ChannelFactory
            .Setup(factory => factory.Create(It.IsAny<string>(), It.IsAny<DuplexChannel<StreamingMessage>>()))
            .Returns((RpcClientWorkerChannel)null);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LinkAsync(registry, "worker").WaitAsync(TestTimeout));

        Assert.Equal("The client worker channel factory returned no channel.", exception.Message);
        TestDuplexChannel<StreamingMessage> transport = Assert.Single(harness.Transports);
        await transport.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task LinkAsync_MismatchedWorkerDisposesChannelAndTransport()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("different-worker");
        harness.Enqueue("worker", channel);
        await using WorkerChannelRegistry registry = harness.Registry;

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LinkAsync(registry, "worker").WaitAsync(TestTimeout));

        Assert.Equal("The client worker channel factory returned worker 'different-worker' for requested worker 'worker'.",
            exception.Message);
        await channel.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(1, channel.DisposeCount);
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task UnlinkAsync_IsIdempotentAndLeavesOtherWorkersLinked()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("worker");
        ChannelControl second = new("replacement");
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;

        await LinkAsync(registry, "worker");
        await LinkAsync(registry, "replacement");

        Assert.True(await registry.UnlinkAsync("worker"));
        Assert.False(await registry.UnlinkAsync("worker"));
        Assert.Equal(1, first.DisposeCount);
        WorkerLinkException terminal = await Assert.ThrowsAsync<WorkerLinkException>(() => LinkAsync(registry, "worker"));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, terminal.Reason);
        Assert.Equal(0, second.DisposeCount);
        Assert.Same(second.Channel, Assert.Single(registry.GetInitializedChannels()));
    }

    [Fact]
    public async Task Completion_RemovesOnlyTerminatedWorkerAndRetainsIdentity()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl other = new("other");
        ChannelControl replacement = new("replacement");
        harness.Enqueue(first);
        harness.Enqueue(other);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;

        await LinkAsync(registry, "first");
        await LinkAsync(registry, "other");
        first.Complete(new InvalidOperationException("transport failed"));
        await first.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => !registry.TryGetInitializedChannel("first", out _));

        Assert.False(registry.TryGetInitializedChannel("first", out _));
        WorkerLinkException terminal = await Assert.ThrowsAsync<WorkerLinkException>(() => LinkAsync(registry, "first"));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, terminal.Reason);
        Assert.Equal(0, other.DisposeCount);
        Assert.True(registry.TryGetInitializedChannel("other", out WorkerChannel healthyChannel));
        Assert.Same(other.Channel, healthyChannel);
        Assert.Same(other.Channel, Assert.Single(registry.GetInitializedChannels()));
        WorkerChannel relinkedChannel = await LinkAsync(registry, "replacement");
        Assert.Same(replacement.Channel, relinkedChannel);
        Assert.Equal(2, registry.GetInitializedChannels().Count);
    }

    [Fact]
    public async Task Completion_DuringMetadataRequest_FailsProvider()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, "worker");
        RpcClientWorkerFunctionMetadataProvider provider = new(
            registry, NullLogger<RpcClientWorkerFunctionMetadataProvider>.Instance, Mock.Of<IWorkerRuntimeResolver>());
        Task<FunctionMetadataResult> metadata = provider.GetFunctionMetadataAsync([]);

        channel.Complete();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => metadata.WaitAsync(TestTimeout));
        Assert.False(registry.TryGetInitializedChannel("worker", out _));
    }

    [Fact]
    public async Task UnlinkAsync_AllowsDifferentWorkerWhileCleanupIsPending()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("worker", blockDisposal: true);
        ChannelControl replacement = new("replacement");
        harness.Enqueue(first);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, "worker");

        Task<bool> unlink = registry.UnlinkAsync("worker");
        await first.DisposeStarted.WaitAsync(TestTimeout);

        try
        {
            WorkerChannel linkedChannel = await LinkAsync(registry, "replacement").WaitAsync(TestTimeout);
            Assert.Same(replacement.Channel, linkedChannel);
            Assert.False(unlink.IsCompleted);
            Assert.Same(replacement.Channel, Assert.Single(registry.GetInitializedChannels()));
        }
        finally
        {
            first.AllowDispose();
            await unlink.WaitAsync(TestTimeout);
        }

        Assert.True(await unlink.WaitAsync(TestTimeout));
        Assert.True(registry.TryGetInitializedChannel("replacement", out WorkerChannel initializedChannel));
        Assert.Same(replacement.Channel, initializedChannel);
    }

    [Fact]
    public async Task WaitForFirstInitializedAsync_SupportsMultipleWaitersCancellationAndReset()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl second = new("second", blockStart: true);
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;
        using CancellationTokenSource cancellationSource = new();

        Task<WorkerChannel> firstWaiter = registry.WaitForFirstInitializedAsync();
        Task<WorkerChannel> secondWaiter = registry.WaitForFirstInitializedAsync();
        Task<WorkerChannel> canceledWaiter = registry.WaitForFirstInitializedAsync(cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter);
        await LinkAsync(registry, "first");
        WorkerChannel[] readyChannels = await Task.WhenAll(firstWaiter, secondWaiter).WaitAsync(TestTimeout);
        Assert.All(readyChannels, channel => Assert.Same(first.Channel, channel));

        Assert.True(await registry.UnlinkAsync("first"));
        Task<WorkerChannel> resetWaiter = registry.WaitForFirstInitializedAsync();
        Task<WorkerChannel> secondLink = LinkAsync(registry, "second");
        await second.StartEntered.WaitAsync(TestTimeout);
        Assert.False(resetWaiter.IsCompleted);
        second.AllowStart();

        await secondLink.WaitAsync(TestTimeout);
        Assert.Same(second.Channel, await resetWaiter.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task GetInitializedChannels_ReturnsCopy()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl second = new("second");
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;

        await LinkAsync(registry, first.Id);

        IReadOnlyList<WorkerChannel> channels = registry.GetInitializedChannels();
        await LinkAsync(registry, second.Id);

        Assert.Same(first.Channel, Assert.Single(channels));
        IReadOnlyList<WorkerChannel> current = registry.GetInitializedChannels();
        Assert.Equal(2, current.Count);
        Assert.Contains(first.Channel, current);
        Assert.Contains(second.Channel, current);
        Assert.NotSame(channels, current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_CancelsLinksAndDisposesChannelsOnce(bool initialized)
    {
        RegistryHarness harness = new();
        ChannelControl ready = new("ready");
        ChannelControl linking = new("worker", blockStart: true);
        harness.Enqueue(ready);
        harness.Enqueue(linking);
        WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, "ready");
        Task<WorkerChannel> link = LinkAsync(registry, "worker");
        await linking.StartEntered.WaitAsync(TestTimeout);
        if (initialized)
        {
            linking.AllowStart();
            await link.WaitAsync(TestTimeout);
        }

        Task firstDispose = registry.DisposeAsync().AsTask();
        Task repeatedDispose = registry.DisposeAsync().AsTask();

        Assert.Same(firstDispose, repeatedDispose);
        if (!initialized)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => link.WaitAsync(TestTimeout));
        }

        await firstDispose.WaitAsync(TestTimeout);
        Assert.Equal(1, ready.DisposeCount);
        Assert.Equal(1, linking.DisposeCount);
        ObjectDisposedException exception =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => LinkAsync(registry, "late"));
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, exception.ObjectName);
        Assert.False(await registry.UnlinkAsync("worker"));
        Assert.False(registry.TryGetInitializedChannel("worker", out _));
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task DisposeAsync_BeforeQueuedLinkStarts_WaitsForOperationCleanup()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        QueuedSynchronizationContext context = new();
        Task<WorkerChannel> link = context.Run(() => LinkAsync(registry, "worker"));
        Task dispose = registry.DisposeAsync().AsTask();

        try
        {
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            context.RunContinuations();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => link.WaitAsync(TestTimeout));
            await dispose.WaitAsync(TestTimeout);
        }

        harness.DuplexFactory.Verify(
            factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task DisposeAsync_ReleasesFirstReadyWaiters()
    {
        RegistryHarness harness = new();
        WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> waiter = registry.WaitForFirstInitializedAsync();

        await registry.DisposeAsync();

        ObjectDisposedException waiterException =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiter.WaitAsync(TestTimeout));
        ObjectDisposedException newWaitException =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.WaitForFirstInitializedAsync());
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, waiterException.ObjectName);
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, newWaitException.ObjectName);
    }

    private static Task<WorkerChannel> LinkAsync(WorkerChannelRegistry registry, string workerId)
        => registry.LinkAsync(workerId, CreateEndpoint(workerId));

    private static Uri CreateEndpoint(string workerId)
        => new($"http://{workerId}.test:5000");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeoutSource.Token);
        }
    }

    private static RpcClientWorkerChannelFactory CreateRealChannelFactory()
    {
        Mock<IScriptHostManager> hostManager = new();
        hostManager.As<IServiceProvider>()
            .Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
            .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = "c:\\test" }));
        Mock<IOptionsMonitor<ScriptApplicationHostOptions>> applicationHostOptions = new();
        applicationHostOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { ScriptPath = "c:\\test" });
        Mock<IAppCapabilitiesStore> appCapabilitiesStore = new();
        appCapabilitiesStore.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>()))
            .Returns(true);

        return new(
            new ScriptEventManager(),
            hostManager.Object,
            Mock.Of<IEnvironment>(),
            NullLoggerFactory.Instance,
            applicationHostOptions.Object,
            Mock.Of<ISharedMemoryManager>(),
            Options.Create(new WorkerConcurrencyOptions()),
            Options.Create(new FunctionsHostingConfigOptions()),
            appCapabilitiesStore.Object,
            Mock.Of<IHttpProxyService>(),
            Mock.Of<IMetricsLogger>());
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _callbacks = new();

        public override void Post(SendOrPostCallback callback, object state)
            => _callbacks.Enqueue((callback, state));

        internal Task<T> Run<T>(Func<Task<T>> callback)
        {
            SynchronizationContext previous = Current;
            SetSynchronizationContext(this);
            try
            {
                return callback();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        internal void RunContinuations()
        {
            while (_callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
            }
        }
    }

    private sealed class RegistryHarness
    {
        private readonly ConcurrentDictionary<Uri, ConcurrentQueue<ChannelControl>> _channels = new();
        private readonly ConcurrentDictionary<DuplexChannel<StreamingMessage>, ChannelControl> _controls = new();
        private readonly RpcClientWorkerChannelFactory _realChannelFactory = CreateRealChannelFactory();
        private readonly ConcurrentQueue<TestDuplexChannel<StreamingMessage>> _transports = new();

        public RegistryHarness()
        {
            DuplexFactory.Setup(factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns((Uri endpoint, CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TestDuplexChannel<StreamingMessage> transport;
                    if (_channels.TryGetValue(endpoint, out ConcurrentQueue<ChannelControl> channels) &&
                        channels.TryDequeue(out ChannelControl channel))
                    {
                        transport = channel.Transport;
                        _controls.TryAdd(transport, channel);
                        channel.BeginHandshake();
                    }
                    else
                    {
                        transport = new();
                    }

                    _transports.Enqueue(transport);

                    return Task.FromResult<DuplexChannel<StreamingMessage>>(transport);
                });
            ChannelFactory
                .Setup(factory => factory.Create(It.IsAny<string>(), It.IsAny<DuplexChannel<StreamingMessage>>()))
                .Returns((string workerId, DuplexChannel<StreamingMessage> ownedChannel) =>
                {
                    if (!_controls.TryGetValue(ownedChannel, out ChannelControl control))
                    {
                        throw new InvalidOperationException($"No test channel was configured for worker '{workerId}'.");
                    }

                    RpcClientWorkerChannel channel = _realChannelFactory.Create(control.Id, ownedChannel);
                    control.Attach(channel);

                    return channel;
                });
            Registry = new(
                DuplexFactory.Object, ChannelFactory.Object, NullLogger<WorkerChannelRegistry>.Instance);
        }

        internal Mock<IRpcClientWorkerChannelFactory> ChannelFactory { get; } = new();

        internal Mock<IDuplexChannelFactory<StreamingMessage>> DuplexFactory { get; } = new();

        internal WorkerChannelRegistry Registry { get; }

        internal IReadOnlyCollection<TestDuplexChannel<StreamingMessage>> Transports => _transports.ToArray();

        internal void Enqueue(ChannelControl channel)
            => Enqueue(channel.Id, channel);

        internal void Enqueue(string workerId, ChannelControl channel)
        {
            ConcurrentQueue<ChannelControl> channels = _channels.GetOrAdd(CreateEndpoint(workerId), static _ => new());
            channels.Enqueue(channel);
        }
    }

    private sealed class ChannelControl
    {
        private readonly TaskCompletionSource _startEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _startRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception _startFailure;
        private RpcClientWorkerChannel _channel;

        internal ChannelControl(string id, bool blockStart = false, bool blockDisposal = false)
        {
            Id = id;
            Transport = new(blockDisposal);
            if (!blockStart)
            {
                _startRelease.TrySetResult();
            }
        }

        internal RpcClientWorkerChannel Channel
            => Interlocked.CompareExchange(ref _channel, null, null);

        internal int DisposeCount => Transport.DisposeCount;

        internal Task DisposeStarted => Transport.DisposeStarted;

        internal string Id { get; }

        internal Task StartEntered => _startEntered.Task;

        internal TestDuplexChannel<StreamingMessage> Transport { get; }

        internal void AllowDispose() => Transport.AllowDispose();

        internal void AllowStart() => _startRelease.TrySetResult();

        internal void Attach(RpcClientWorkerChannel channel)
        {
            if (Interlocked.CompareExchange(ref _channel, channel, null) is not null)
            {
                throw new InvalidOperationException("A channel is already attached.");
            }
        }

        internal void BeginHandshake()
        {
            _ = DriveHandshakeAsync();
        }

        internal void Complete(Exception exception = null) => Transport.CompleteResponses(exception);

        internal void FailStart(Exception exception)
        {
            Interlocked.Exchange(ref _startFailure, exception);
            _startRelease.TrySetResult();
        }

        private async Task DriveHandshakeAsync()
        {
            try
            {
                await Transport.SendResponseAsync(new() { StartStream = new() { WorkerId = Id } });
                await Transport.Requests.ReadAsync().AsTask();
                _startEntered.TrySetResult();

                Task completed = await Task.WhenAny(_startRelease.Task, Transport.DisposeStarted);
                if (completed != _startRelease.Task)
                {
                    return;
                }

                Exception startFailure = Interlocked.CompareExchange(ref _startFailure, null, null);
                if (startFailure is not null)
                {
                    Transport.CompleteResponses(startFailure);
                    return;
                }

                await Transport.SendResponseAsync(new StreamingMessage
                {
                    WorkerInitResponse = new WorkerInitResponse
                    {
                        Result = new() { Status = StatusResult.Types.Status.Success },
                    },
                });
            }
            catch (ChannelClosedException)
            {
                // Transport teardown can win before a deliberately blocked test handshake completes.
            }
        }
    }
}
