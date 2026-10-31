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
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
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
public sealed partial class WorkerChannelRegistryTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task LinkAsync_PassesConnectedTransportToChannelFactory()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await registry.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);

        harness.ChannelFactory.Verify(factory => factory.Create("worker", channel.Transport), Times.Once);
    }

    [Fact]
    public async Task LinkAsync_PublishesCompletionBeforeConnectingOutsideStateLock()
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        IOException failure = new("connection failed");
        Task<WorkerLinkResult> replay = null;
        harness.DuplexFactory.Setup(factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns((Uri endpoint, CancellationToken _) =>
            {
                // Another thread must be able to enter the registry even if connection setup runs synchronously.
                Task<IReadOnlyList<WorkerChannel>> snapshot = Task.Run(registry.GetInitializedChannels);
                Assert.Empty(snapshot.WaitAsync(TestTimeout).GetAwaiter().GetResult());
                replay = registry.LinkAsync("worker", endpoint);
                Assert.False(replay.IsCompleted);

                return Task.FromException<DuplexChannel<StreamingMessage>>(failure);
            });

        Task<WorkerLinkResult> first = registry.LinkAsync("worker", CreateEndpoint("worker"));
        IOException firstFailure = await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TestTimeout));
        IOException replayFailure = await Assert.ThrowsAsync<IOException>(() => replay.WaitAsync(TestTimeout));

        Assert.Same(failure, firstFailure);
        Assert.Same(failure, replayFailure);
        harness.DuplexFactory.Verify(
            factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.ChannelFactory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://worker-proxy:28080")]
    [InlineData("http://worker-proxy:28080/path")]
    [InlineData("http://worker-proxy:28080?query=value")]
    public async Task LinkAsync_InvalidGrpcEndpointDoesNotConnect(string endpoint)
    {
        RegistryHarness harness = new();
        await using WorkerChannelRegistry registry = harness.Registry;
        Uri grpcEndpoint = new(endpoint, UriKind.RelativeOrAbsolute);

        await Assert.ThrowsAsync<ArgumentException>(
            () => registry.LinkAsync("worker", grpcEndpoint));

        harness.DuplexFactory.VerifyNoOtherCalls();
        harness.ChannelFactory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LinkAsync_ConnectionDeadlineReturnsTimeoutWithoutCreatingChannel()
    {
        RegistryHarness harness = new(TimeSpan.FromMilliseconds(50));
        TaskCompletionSource<DuplexChannel<StreamingMessage>> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken connectionToken = default;
        harness.DuplexFactory.Setup(factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns((Uri _, CancellationToken token) =>
            {
                connectionToken = token;

                return connection.Task.WaitAsync(token);
            });
        await using WorkerChannelRegistry registry = harness.Registry;

        await Assert.ThrowsAsync<TimeoutException>(() => registry.LinkAsync("worker", CreateEndpoint("worker"))
            .WaitAsync(TestTimeout));

        Assert.True(connectionToken.IsCancellationRequested);
        Assert.Empty(registry.GetInitializedChannels());
        harness.ChannelFactory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4294967295)]
    public void Constructor_InvalidTimeoutThrows(double milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RegistryHarness(TimeSpan.FromMilliseconds(milliseconds)));

    [Theory]
    [InlineData("http://worker:50053", "http://worker:50053")]
    [InlineData("http://worker-proxy:28080", "http://worker-proxy:28080/")]
    [InlineData("http://worker-proxy", "HTTP://WORKER-PROXY:80/")]
    [InlineData("https://worker-proxy", "HTTPS://WORKER-PROXY:443/")]
    public async Task LinkAsync_ConcurrentAndCompletedReplayShareInitialization(string endpoint, string retryEndpoint)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(new Uri(endpoint), channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        Uri grpcEndpoint = new(endpoint);
        Uri retryGrpcEndpoint = new(retryEndpoint);

        Task<WorkerLinkResult> first = linker.LinkAsync("worker", grpcEndpoint);
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerLinkResult> replay = linker.LinkAsync("worker", retryGrpcEndpoint);
        Assert.False(first.IsCompleted);
        Assert.False(replay.IsCompleted);
        channel.AllowStart();
        WorkerLinkResult linked = await first.WaitAsync(TestTimeout);
        WorkerLinkResult pendingReplay = await replay.WaitAsync(TestTimeout);
        WorkerLinkResult completedReplay = await linker.LinkAsync("worker", retryGrpcEndpoint);

        Assert.True(linked.IsNewLink);
        Assert.False(pendingReplay.IsNewLink);
        Assert.False(completedReplay.IsNewLink);
        Assert.Same(linked.Channel, pendingReplay.Channel);
        Assert.Same(linked.Channel, completedReplay.Channel);
        Assert.Single(harness.Transports);
        Assert.Single(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task LinkAsync_ConcurrentRequestsHaveExactlyOneCreator()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<WorkerLinkResult>[] links = [.. Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            await release.Task;

            return await registry.LinkAsync("worker", CreateEndpoint("worker"));
        }))];

        release.SetResult();
        await channel.StartEntered.WaitAsync(TestTimeout);
        channel.AllowStart();
        WorkerLinkResult[] results = await Task.WhenAll(links).WaitAsync(TestTimeout);

        Assert.Single(results, result => result.IsNewLink);
        Assert.All(results, result => Assert.Same(channel.Channel, result.Channel));
        Assert.Single(harness.Transports);
    }

    [Fact]
    public async Task LinkAsync_ConflictingEndpointIsRejectedWhilePendingAndReady()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        Task<WorkerLinkResult> first = linker.LinkAsync("worker", CreateEndpoint("worker"));
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

    [Theory]
    [InlineData("http://worker-proxy:28080", "http://other-proxy:28080")]
    [InlineData("http://worker-proxy:28080", "https://worker-proxy:28080")]
    [InlineData("http://worker-proxy:28080", "http://worker-proxy:28081")]
    public async Task LinkAsync_ConflictingGrpcEndpointIsRejectedWhilePendingReadyAndTerminal(string endpoint, string retryEndpoint)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(new Uri(endpoint), channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        Uri grpcEndpoint = new(endpoint);
        Uri retryGrpcEndpoint = new(retryEndpoint);
        Task<WorkerLinkResult> first = registry.LinkAsync("worker", grpcEndpoint);
        await channel.StartEntered.WaitAsync(TestTimeout);

        WorkerLinkException pendingConflict = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync("worker", retryGrpcEndpoint));
        Assert.Equal(WorkerLinkFailureReason.Conflict, pendingConflict.Reason);
        Assert.False(first.IsCompleted);
        Assert.Equal(0, channel.DisposeCount);

        channel.AllowStart();
        WorkerLinkResult linked = await first.WaitAsync(TestTimeout);
        WorkerLinkException readyConflict = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync("worker", retryGrpcEndpoint));
        Assert.Equal(WorkerLinkFailureReason.Conflict, readyConflict.Reason);
        WorkerLinkResult replay = await registry.LinkAsync("worker", grpcEndpoint);
        Assert.False(replay.IsNewLink);
        Assert.Same(linked.Channel, replay.Channel);
        Assert.Equal(0, channel.DisposeCount);

        channel.Complete();
        await channel.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => !registry.TryGetInitializedChannel("worker", out _));
        WorkerLinkException terminalConflict = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync("worker", retryGrpcEndpoint));
        Assert.Equal(WorkerLinkFailureReason.Conflict, terminalConflict.Reason);
        WorkerLinkException terminated = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync("worker", grpcEndpoint));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, terminated.Reason);
        Assert.Single(harness.Transports);
        harness.DuplexFactory.Verify(factory => factory.ConnectAsync(grpcEndpoint, It.IsAny<CancellationToken>()), Times.Once);
        harness.DuplexFactory.VerifyNoOtherCalls();
        harness.ChannelFactory.Verify(factory => factory.Create("worker", channel.Transport), Times.Once);
        harness.ChannelFactory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("http://worker-proxy:28080", "http://worker-proxy:28080")]
    [InlineData("http://worker-proxy:28080", "http://other-proxy:28080")]
    public async Task LinkAsync_FailedHandshakeCleansBeforeRetry(string endpoint, string retryEndpoint)
    {
        RegistryHarness harness = new();
        ChannelControl failed = new("worker", blockStart: true);
        ChannelControl replacement = new("worker");
        harness.Enqueue(new Uri(endpoint), failed);
        harness.Enqueue(new Uri(retryEndpoint), replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        TimeoutException failure = new("WorkerInit timed out.");
        Uri grpcEndpoint = new(endpoint);
        Uri retryGrpcEndpoint = new(retryEndpoint);
        Task<WorkerLinkResult> link = linker.LinkAsync("worker", grpcEndpoint);
        await failed.StartEntered.WaitAsync(TestTimeout);
        failed.FailStart(failure);

        TimeoutException actual = await Assert.ThrowsAsync<TimeoutException>(() => link.WaitAsync(TestTimeout));
        Assert.Same(failure, actual);
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Equal(1, failed.DisposeCount);
        WorkerLinkResult linked = await linker.LinkAsync("worker", retryGrpcEndpoint).WaitAsync(TestTimeout);
        Assert.True(linked.IsNewLink);
        Assert.Same(replacement.Channel, linked.Channel);
        Assert.Single(registry.GetInitializedChannels());
        harness.ChannelFactory.Verify(factory => factory.Create("worker", replacement.Transport), Times.Once);
    }

    [Fact]
    public async Task LinkAsync_CleanCloseDuringHandshakeThrowsIOException()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerLinkResult> link = registry.LinkAsync("worker", CreateEndpoint("worker"));
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
        Task<WorkerLinkResult> first = linker.LinkAsync("worker", CreateEndpoint("worker"));
        await channel.StartEntered.WaitAsync(TestTimeout);
        Task<WorkerLinkResult> replay = linker.LinkAsync("worker", CreateEndpoint("worker"), cancellationToken: cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replay);
        Assert.False(first.IsCompleted);
        channel.AllowStart();
        WorkerLinkResult linked = await first.WaitAsync(TestTimeout);
        Assert.True(linked.IsNewLink);
        Assert.Same(channel.Channel, linked.Channel);
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
        Task<WorkerLinkResult> first = linker.LinkAsync("worker", CreateEndpoint("worker"),
            cancellationToken: cancellation.Token);
        await canceled.StartEntered.WaitAsync(TestTimeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestTimeout));
        Assert.Equal(1, canceled.DisposeCount);
        Assert.Empty(registry.GetInitializedChannels());
        WorkerLinkResult linked = await linker.LinkAsync("worker", CreateEndpoint("worker")).WaitAsync(TestTimeout);
        Assert.True(linked.IsNewLink);
        Assert.Same(replacement.Channel, linked.Channel);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), false)]
    [InlineData(typeof(TimeoutException), false)]
    [InlineData(typeof(IOException), false)]
    [InlineData(typeof(IOException), true)]
    public async Task LinkAsync_InvalidatedWhileWaitingForStateLockDoesNotPublish(Type expectedExceptionType, bool faultTransport)
    {
        RegistryHarness harness = new(expectedExceptionType == typeof(TimeoutException) ? TestTimeout / 2 : null);
        ChannelControl first = new("first", blockSetup: true);
        ChannelControl waiting = new("waiting");
        harness.Enqueue(first);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);

        // Complete WorkerInit so the resumed link cannot suspend between the factory and the registration lock.
        await using RpcClientWorkerChannel waitingChannel = CreateRealChannelFactory(waiting.MetricsLogger.Object)
            .Create(waiting.Id, waiting.Transport);
        waiting.Attach(waitingChannel);
        waiting.BeginHandshake();
        await waitingChannel.StartAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Uri endpoint = CreateEndpoint(waiting.Id);
        TaskCompletionSource<DuplexChannel<StreamingMessage>> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<CancellationToken> connectionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Thread> registrationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.DuplexFactory.Setup(factory => factory.ConnectAsync(endpoint, It.IsAny<CancellationToken>()))
            .Returns((Uri _, CancellationToken token) =>
            {
                connectionStarted.SetResult(token);
                return connection.Task;
            });
        harness.ChannelFactory.Setup(factory => factory.Create(waiting.Id, waiting.Transport))
            .Returns(() =>
            {
                registrationStarted.SetResult(Thread.CurrentThread);
                return waitingChannel;
            });
        using CancellationTokenSource cancellation = new();
        Task<WorkerLinkResult> link = Task.Run(() => registry.LinkAsync(waiting.Id, endpoint, cancellation.Token));
        CancellationToken operationToken = await connectionStarted.Task.WaitAsync(TestTimeout);
        IOException transportFailure = faultTransport ? new("transport failed") : null;
        Task activation = Task.Run(() => registry.ActivateAsync(
            [CreateFunction("TestFunction")], new ManagedDependencyOptions(), functionTimeout: null));
        try
        {
            await first.SetupEntered.WaitAsync(TestTimeout);
            connection.SetResult(waiting.Transport);
            Thread registrationThread = await registrationStarted.Task.WaitAsync(TestTimeout);
            await WaitUntilAsync(() => registrationThread.ThreadState.HasFlag(ThreadState.WaitSleepJoin));
            Assert.False(operationToken.IsCancellationRequested);
            Assert.False(link.IsCompleted);

            if (expectedExceptionType == typeof(OperationCanceledException))
            {
                cancellation.Cancel();
            }
            else if (expectedExceptionType == typeof(TimeoutException))
            {
                TaskCompletionSource deadlineElapsed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = operationToken.Register(() => deadlineElapsed.TrySetResult());
                await deadlineElapsed.Task.WaitAsync(TestTimeout);
            }
            else
            {
                waiting.Complete(transportFailure);
                await WaitUntilAsync(() => waitingChannel.Completion.IsCompleted);
            }
        }
        finally
        {
            first.AllowSetup();
            await activation.WaitAsync(TestTimeout);
        }

        if (expectedExceptionType == typeof(OperationCanceledException))
        {
            OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => link.WaitAsync(TestTimeout));
            Assert.Equal(operationToken, exception.CancellationToken);
        }
        else if (expectedExceptionType == typeof(TimeoutException))
        {
            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => link.WaitAsync(TestTimeout));
            Assert.Equal("The worker link initialization deadline expired.", exception.Message);
            Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        }
        else
        {
            IOException exception = await Assert.ThrowsAsync<IOException>(() => link.WaitAsync(TestTimeout));
            Assert.Same(transportFailure, exception.InnerException);
        }

        Assert.Equal(1, waiting.DisposeCount);
        Assert.Equal(0, waiting.SetupAttempts);
        Assert.False(registry.TryGetInitializedChannel(waiting.Id, out _));
        Assert.Same(first.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Same(first.Channel, Assert.Single(await registry.GetReadyChannelsAsync()));

        ChannelControl replacement = new(waiting.Id);
        Uri replacementEndpoint = CreateEndpoint("replacement");
        harness.Enqueue(replacementEndpoint, replacement);
        WorkerLinkResult retry = await registry.LinkAsync(waiting.Id, replacementEndpoint).WaitAsync(TestTimeout);
        Assert.True(retry.IsNewLink);
        Assert.Same(replacement.Channel, retry.Channel);
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
    public async Task FunctionLoadFailure_RemovesOnlyFailedChannelAndRetainsTerminalIdentity()
    {
        RegistryHarness harness = new();
        ChannelControl failed = new("failed");
        ChannelControl healthy = new("healthy");
        harness.Enqueue(failed);
        harness.Enqueue(healthy);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, failed.Id);
        await LinkAsync(registry, healthy.Id);
        FunctionMetadata function = CreateFunction("TestFunction");
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        object healthyBuffer = healthy.Channel.FunctionInputBuffers[function.GetFunctionId()];
        Task<List<RawFunctionMetadata>> metadata = failed.Channel.GetFunctionMetadata();
        await failed.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest);

        Exception failure = await failed.FailFunctionLoadAsync();
        await failed.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => !registry.TryGetInitializedChannel(failed.Id, out _));

        Exception metadataFailure = await Assert.ThrowsAsync<KeyNotFoundException>(() => metadata.WaitAsync(TestTimeout));
        Assert.Same(failure, metadataFailure);
        Assert.Equal(1, failed.DisposeCount);
        Assert.Equal(0, healthy.DisposeCount);
        Assert.Same(healthy.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Same(healthy.Channel, Assert.Single(await registry.GetReadyChannelsAsync()));
        Assert.Same(healthyBuffer, healthy.Channel.FunctionInputBuffers[function.GetFunctionId()]);
        WorkerLinkException terminated = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync(failed.Id, CreateEndpoint(failed.Id)));
        Assert.Equal(WorkerLinkFailureReason.WorkerTerminated, terminated.Reason);
        Assert.Equal(2, harness.Transports.Count);
    }

    [Fact]
    public async Task LinkAsync_DisposalRejectsNewAndPendingLinks()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        IWorkerChannelRegistry linker = registry;
        Task<WorkerLinkResult> link = linker.LinkAsync("worker", CreateEndpoint("worker"));
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
        Task<WorkerLinkResult> replay = ((IWorkerChannelRegistry)registry).LinkAsync("worker", CreateEndpoint("worker"));
        Assert.False(replay.IsCompleted);
        harness.ChannelFactory.Verify(factory => factory.Create("worker", It.IsAny<DuplexChannel<StreamingMessage>>()), Times.Once);
        channel.AllowStart();
        WorkerChannel initialized = await firstLink.WaitAsync(TestTimeout);
        WorkerLinkResult replayResult = await replay.WaitAsync(TestTimeout);
        Assert.False(replayResult.IsNewLink);
        Assert.Same(initialized, replayResult.Channel);
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
    public async Task UnlinkAsync_WhileInitializationIsPending_WaitsWithoutBlockingOtherWorkers()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        ChannelControl other = new("other");
        harness.Enqueue(channel);
        harness.Enqueue(other);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> link = LinkAsync(registry, "worker");
        Task<WorkerLinkResult> replay = registry.LinkAsync("worker", CreateEndpoint("worker"));
        Task<bool> unlink = null;

        try
        {
            await channel.StartEntered.WaitAsync(TestTimeout);
            Assert.Single(harness.Transports);
            unlink = registry.UnlinkAsync("worker");
            Assert.False(unlink.IsCompleted);
            Assert.False(replay.IsCompleted);
            WorkerChannel otherChannel = await LinkAsync(registry, "other").WaitAsync(TestTimeout);
            Assert.Same(other.Channel, otherChannel);
            Assert.False(link.IsCompleted);
            Assert.False(unlink.IsCompleted);
        }
        finally
        {
            channel.AllowStart();
            await link.WaitAsync(TestTimeout);
            WorkerLinkResult replayResult = await replay.WaitAsync(TestTimeout);
            Assert.False(replayResult.IsNewLink);
            Assert.Same(await link, replayResult.Channel);
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
    public async Task LinkAsync_CanceledWhileUnlinkIsWaiting_ReleasesReservationAndAllowsRetry()
    {
        RegistryHarness harness = new();
        ChannelControl canceled = new("worker", blockStart: true);
        ChannelControl replacement = new("worker");
        harness.Enqueue(canceled);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        using CancellationTokenSource cancellation = new();
        Task<WorkerLinkResult> link = registry.LinkAsync("worker", CreateEndpoint("worker"), cancellationToken: cancellation.Token);
        Task<bool> unlink = registry.UnlinkAsync("worker");

        try
        {
            await canceled.StartEntered.WaitAsync(TestTimeout);
            Assert.False(unlink.IsCompleted);
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => link.WaitAsync(TestTimeout));
            Assert.True(link.IsCanceled);
            await unlink.WaitAsync(TestTimeout);
        }

        Assert.False(await unlink);
        harness.DuplexFactory.Verify(
            factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, canceled.DisposeCount);
        WorkerChannel linked = await LinkAsync(registry, "worker").WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, linked);
        Assert.Equal(2, harness.Transports.Count);
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
            registry, NullLogger<RpcClientWorkerFunctionMetadataProvider>.Instance, Mock.Of<IWorkerRuntimeResolver>(),
            Options.Create(new FunctionMetadataOptions { UseWorkerRuntimeFromMetadata = true }));
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

    [Fact]
    public async Task ActivateAsync_EmptyFunctionsConfiguresInitializedChannels()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, channel.Id);

        await registry.ActivateAsync([], new ManagedDependencyOptions(), functionTimeout: null);

        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels = registry.GetReadyChannelsAsync();
        Assert.True(readyChannels.IsCompletedSuccessfully);
        Assert.Same(channel.Channel, Assert.Single(readyChannels.Result));
        Assert.Empty(channel.Channel.FunctionInputBuffers);
        Assert.Equal(0, channel.SetupAttempts);
    }

    [Fact]
    public async Task ActivateAsync_PartialFailurePublishesOnlySuccessfulChannels()
    {
        InvalidOperationException setupFailure = new("first setup failed");
        RegistryHarness harness = new();
        ChannelControl failed = new("failed", setupFailureCount: 1, setupFailure: setupFailure);
        ChannelControl ready = new("ready");
        harness.Enqueue(failed);
        harness.Enqueue(ready);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, failed.Id);
        await LinkAsync(registry, ready.Id);
        FunctionMetadata function = CreateFunction("TestFunction");

        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);

        StreamingMessage loadRequest = await ready.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        Assert.Equal(function.GetFunctionId(), loadRequest.FunctionLoadRequest.FunctionId);
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels = registry.GetReadyChannelsAsync();
        Assert.True(readyChannels.IsCompletedSuccessfully);
        Assert.Same(ready.Channel, Assert.Single(readyChannels.Result));
        Assert.Equal(1, failed.SetupAttempts);

        Assert.Same(ready.Channel, Assert.Single((await registry.GetReadyChannelsAsync()).ToArray()));
        Assert.Equal(1, failed.SetupAttempts);
    }

    [Fact]
    public async Task ActivateAsync_AllFailuresRethrowsFirstOriginalException()
    {
        InvalidOperationException firstFailure = new("first setup failed");
        IOException secondFailure = new("second setup failed");
        RegistryHarness harness = new();
        ChannelControl first = new("first", setupFailureCount: 1, setupFailure: firstFailure);
        ChannelControl second = new("second", setupFailureCount: 1, setupFailure: secondFailure);
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);
        await LinkAsync(registry, second.Id);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ActivateAsync([CreateFunction("TestFunction")], new ManagedDependencyOptions(), functionTimeout: null));

        Assert.Same(firstFailure, exception);
        Assert.Equal(1, first.SetupAttempts);
        Assert.Equal(1, second.SetupAttempts);
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);
    }

    [Fact]
    public async Task GetReadyChannelsAsync_SharesWaitAndThenCompletesSynchronously()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await registry.ActivateAsync(
            [CreateFunction("TestFunction")],
            new ManagedDependencyOptions(),
            functionTimeout: null);

        ValueTask<IReadOnlyList<WorkerChannel>> firstWait = registry.GetReadyChannelsAsync();
        ValueTask<IReadOnlyList<WorkerChannel>> secondWait = registry.GetReadyChannelsAsync();
        Assert.False(firstWait.IsCompleted);
        Assert.Same(firstWait.AsTask(), secondWait.AsTask());

        await LinkAsync(registry, channel.Id);
        IReadOnlyList<WorkerChannel> signaledChannels = await firstWait.AsTask().WaitAsync(TestTimeout);

        Assert.Same(channel.Channel, Assert.Single(signaledChannels));
        ValueTask<IReadOnlyList<WorkerChannel>> currentChannels = registry.GetReadyChannelsAsync();
        Assert.True(currentChannels.IsCompletedSuccessfully);
        Assert.Same(channel.Channel, Assert.Single(currentChannels.Result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReadyChannelsAsync_DuringSetupDoesNotBlockCaller(bool laterLink)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockSetup: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("TestFunction");
        Task setup;
        if (laterLink)
        {
            await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
            setup = LinkAsync(registry, channel.Id);
        }
        else
        {
            await LinkAsync(registry, channel.Id);
            setup = Task.Run(() => registry.ActivateAsync(
                [function], new ManagedDependencyOptions(), functionTimeout: null));
        }

        await channel.SetupEntered.WaitAsync(TestTimeout);
        Task<ValueTask<IReadOnlyList<WorkerChannel>>> read = Task.Run(() => registry.GetReadyChannelsAsync());
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels;
        try
        {
            readyChannels = await read.WaitAsync(TestTimeout);
            Assert.False(readyChannels.IsCompleted);
            using CancellationTokenSource cancellation = new();
            Task<IReadOnlyList<WorkerChannel>> canceledWait = readyChannels.AsTask().WaitAsync(cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
            Assert.False(readyChannels.IsCompleted);
        }
        finally
        {
            channel.AllowSetup();
            await setup.WaitAsync(TestTimeout);
            await read.WaitAsync(TestTimeout);
        }

        Assert.Same(channel.Channel, Assert.Single(await readyChannels.AsTask().WaitAsync(TestTimeout)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReadyChannelsAsync_DuringLaterSetupHandlesExistingSnapshot(bool stopFirst)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl second = new("second", blockSetup: true);
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);
        await registry.ActivateAsync(
            [CreateFunction("TestFunction")], new ManagedDependencyOptions(), functionTimeout: null);
        Task<WorkerChannel> secondLink = LinkAsync(registry, second.Id);
        await second.SetupEntered.WaitAsync(TestTimeout);
        Task<ValueTask<IReadOnlyList<WorkerChannel>>> read = null;
        ValueTask<IReadOnlyList<WorkerChannel>> readyChannels;
        try
        {
            if (stopFirst)
            {
                await first.Channel.DisposeAsync();
            }

            read = Task.Run(() => registry.GetReadyChannelsAsync());
            readyChannels = await read.WaitAsync(TestTimeout);
            if (stopFirst)
            {
                Assert.False(readyChannels.IsCompleted);
            }
            else
            {
                Assert.True(readyChannels.IsCompletedSuccessfully);
                Assert.Same(first.Channel, Assert.Single(readyChannels.Result));
            }
        }
        finally
        {
            second.AllowSetup();
            await secondLink.WaitAsync(TestTimeout);
            if (read is not null)
            {
                await read.WaitAsync(TestTimeout);
            }
        }

        // Link completion does not wait for background setup; this locked read observes its completed publication.
        Assert.Contains(second.Channel, registry.GetInitializedChannels());
        IReadOnlyList<WorkerChannel> snapshot = await registry.GetReadyChannelsAsync();
        Assert.Equal(stopFirst ? 1 : 2, snapshot.Count);
        Assert.Contains(second.Channel, snapshot);
        if (stopFirst)
        {
            Assert.Same(second.Channel, Assert.Single(await readyChannels.AsTask().WaitAsync(TestTimeout)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReadyChannelsAsync_ExcludesTerminatedChannelDuringSetup(bool functionLoadFailure)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl healthy = new("healthy");
        ChannelControl later = new("later", blockSetup: true);
        harness.Enqueue(first);
        harness.Enqueue(healthy);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);
        await LinkAsync(registry, healthy.Id);
        await registry.ActivateAsync([CreateFunction("TestFunction")], new ManagedDependencyOptions(), functionTimeout: null);
        Task<WorkerChannel> laterLink = LinkAsync(registry, later.Id);
        try
        {
            await later.SetupEntered.WaitAsync(TestTimeout);
            if (functionLoadFailure)
            {
                await first.FailFunctionLoadAsync();
                Assert.False(first.Channel.Completion.IsCompleted);
            }
            else
            {
                first.Complete();
                await first.Channel.Completion.WaitAsync(TestTimeout);
            }

            ValueTask<IReadOnlyList<WorkerChannel>> snapshot = registry.GetReadyChannelsAsync();
            Assert.True(snapshot.IsCompletedSuccessfully);
            Assert.Contains(healthy.Channel, snapshot.Result);
            Assert.DoesNotContain(first.Channel, snapshot.Result);
        }
        finally
        {
            later.AllowSetup();
            await laterLink.WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReadyChannelsAsync_TerminatedChannelDuringSetupReturnsSharedWait(bool functionLoadFailure)
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl later = new("later", blockSetup: true);
        harness.Enqueue(first);
        harness.Enqueue(later);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);
        await registry.ActivateAsync([CreateFunction("TestFunction")], new ManagedDependencyOptions(), functionTimeout: null);
        Task<WorkerChannel> laterLink = LinkAsync(registry, later.Id);
        Task<IReadOnlyList<WorkerChannel>> readyWait;
        try
        {
            await later.SetupEntered.WaitAsync(TestTimeout);
            if (functionLoadFailure)
            {
                await first.FailFunctionLoadAsync();
                Assert.False(first.Channel.Completion.IsCompleted);
            }
            else
            {
                first.Complete();
                await first.Channel.Completion.WaitAsync(TestTimeout);
            }

            readyWait = registry.GetReadyChannelsAsync().AsTask();
            Assert.False(readyWait.IsCompleted);
            Assert.Same(readyWait, registry.GetReadyChannelsAsync().AsTask());
        }
        finally
        {
            later.AllowSetup();
            await laterLink.WaitAsync(TestTimeout);
        }

        Assert.Same(later.Channel, Assert.Single(await readyWait.WaitAsync(TestTimeout)));
    }

    [Fact]
    public async Task GetReadyChannelsAsync_PendingWaitSurvivesContextReplacement()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<IReadOnlyList<WorkerChannel>> initialWait = registry.GetReadyChannelsAsync().AsTask();

        await registry.ActivateAsync([], new ManagedDependencyOptions(), functionTimeout: null);
        FunctionMetadata function = CreateFunction("ReplacementFunction");
        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);

        Assert.Same(initialWait, registry.GetReadyChannelsAsync().AsTask());
        await LinkAsync(registry, channel.Id);

        Assert.Same(channel.Channel, Assert.Single(await initialWait.WaitAsync(TestTimeout)));
        StreamingMessage request = await channel.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        Assert.Equal(function.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
    }

    [Fact]
    public async Task GetReadyChannelsAsync_ReplacesStaleCompletedSignal()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl second = new("second");
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;
        await registry.ActivateAsync(
            [CreateFunction("TestFunction")],
            new ManagedDependencyOptions(),
            functionTimeout: null);
        Task<IReadOnlyList<WorkerChannel>> firstSignal = registry.GetReadyChannelsAsync().AsTask();
        await LinkAsync(registry, first.Id);
        Assert.Same(first.Channel, Assert.Single(await firstSignal.WaitAsync(TestTimeout)));

        Assert.True(await registry.UnlinkAsync(first.Id));
        Task<IReadOnlyList<WorkerChannel>> replacementSignal = registry.GetReadyChannelsAsync().AsTask();
        Assert.False(replacementSignal.IsCompleted);
        Assert.NotSame(firstSignal, replacementSignal);

        await LinkAsync(registry, second.Id);

        Assert.Same(second.Channel, Assert.Single(await replacementSignal.WaitAsync(TestTimeout)));
    }

    [Fact]
    public async Task LinkAsync_BeforeActivationIsConfiguredByActivationSnapshot()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, channel.Id);
        Assert.Equal(0, channel.SetupAttempts);
        Assert.False(channel.Transport.Requests.TryRead(out _));
        FunctionMetadata function = CreateFunction("TestFunction");

        await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);

        StreamingMessage request = await channel.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        Assert.Equal(function.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
        Assert.Same(channel.Channel, Assert.Single((await registry.GetReadyChannelsAsync()).ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_DeferredSetupHandlesActivationAndDisposal(bool disposeBeforeSetup)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata function = CreateFunction("TestFunction");
        if (disposeBeforeSetup)
        {
            await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
        }

        TaskCompletionSource<Action> deferredSetup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<SynchronizationContext> context = new();
        int captureSetup = 1;
        context.Setup(value => value.Post(It.IsAny<SendOrPostCallback>(), It.IsAny<object>()))
            .Callback((SendOrPostCallback callback, object state) =>
            {
                if (Interlocked.CompareExchange(ref captureSetup, 0, 0) == 1 &&
                    registry.TryGetInitializedChannel(channel.Id, out _) &&
                    Interlocked.CompareExchange(ref captureSetup, 0, 1) == 1)
                {
                    // Hold the yielded setup continuation after publication, without holding the registry lock.
                    deferredSetup.SetResult(() => callback(state));
                    return;
                }

                _ = Task.Run(() =>
                {
                    SynchronizationContext previous = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(context.Object);
                    try
                    {
                        callback(state);
                    }
                    finally
                    {
                        SynchronizationContext.SetSynchronizationContext(previous);
                    }
                });
            });

        Task<WorkerChannel> link;
        SynchronizationContext previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context.Object);
        try
        {
            link = LinkAsync(registry, channel.Id);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        bool setupResumed = false;
        try
        {
            Action resumeSetup = await deferredSetup.Task.WaitAsync(TestTimeout);
            Assert.Same(channel.Channel, await link.WaitAsync(TestTimeout));
            if (disposeBeforeSetup)
            {
                await registry.DisposeAsync();
            }
            else
            {
                await registry.ActivateAsync([function], new ManagedDependencyOptions(), functionTimeout: null);
            }

            setupResumed = true;
            resumeSetup();
            Assert.Equal(disposeBeforeSetup ? 0 : 1, channel.SetupAttempts);
            if (disposeBeforeSetup)
            {
                Assert.Empty(channel.Channel.FunctionInputBuffers);
                Assert.Equal(1, channel.DisposeCount);
            }
            else
            {
                StreamingMessage request = await channel.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
                Assert.Equal(function.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
                Assert.False(channel.Transport.Requests.TryRead(out _));
                Assert.Same(channel.Channel, Assert.Single(await registry.GetReadyChannelsAsync()));
            }
        }
        finally
        {
            if (!setupResumed && deferredSetup.Task.IsCompletedSuccessfully)
            {
                deferredSetup.Task.Result();
            }
        }
    }

    [Fact]
    public async Task LinkAsync_AfterActivationReturnsBeforeBackgroundSetupCompletes()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockSetup: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await registry.ActivateAsync(
            [CreateFunction("TestFunction")],
            new ManagedDependencyOptions(),
            functionTimeout: null);

        Task<WorkerChannel> link = LinkAsync(registry, channel.Id);
        try
        {
            await channel.SetupEntered.WaitAsync(TestTimeout);
            Assert.Same(channel.Channel, await link.WaitAsync(TestTimeout));
        }
        finally
        {
            channel.AllowSetup();
        }

        Assert.Same(channel.Channel, Assert.Single(
            await registry.GetReadyChannelsAsync().AsTask().WaitAsync(TestTimeout)));
    }

    [Fact]
    public async Task LinkAsync_BackgroundSetupWinsLockBeforeLinkFinalization()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockSetup: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await registry.ActivateAsync([CreateFunction("TestFunction")], new ManagedDependencyOptions(), functionTimeout: null);
        Mock<SynchronizationContext> context = new();
        int forceSetupOrder = 1;
        context.Setup(value => value.Post(It.IsAny<SendOrPostCallback>(), It.IsAny<object>()))
            .Callback((SendOrPostCallback callback, object state) =>
            {
                bool waitForSetup = Interlocked.CompareExchange(ref forceSetupOrder, 0, 0) == 1 &&
                    !channel.SetupEntered.IsCompleted &&
                    channel.Channel?.StartAsync(CancellationToken.None).IsCompletedSuccessfully == true;
                _ = Task.Run(() =>
                {
                    SynchronizationContext previous = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(context.Object);
                    try
                    {
                        callback(state);
                    }
                    finally
                    {
                        SynchronizationContext.SetSynchronizationContext(previous);
                    }
                });

                if (waitForSetup)
                {
                    // Let the yielded setup continuation take the state lock before its scheduling caller proceeds.
                    channel.SetupEntered.WaitAsync(TestTimeout).GetAwaiter().GetResult();
                }
            });

        Task<WorkerChannel> link;
        SynchronizationContext previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context.Object);
        try
        {
            link = LinkAsync(registry, channel.Id);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        try
        {
            await channel.SetupEntered.WaitAsync(TestTimeout);
            Assert.Same(channel.Channel, await link.WaitAsync(TestTimeout));
        }
        finally
        {
            Interlocked.Exchange(ref forceSetupOrder, 0);
            channel.AllowSetup();
            await link.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task LaterLinkSetupFailureDoesNotFailLinkOrRetryWithinContext()
    {
        InvalidOperationException setupFailure = new("setup failed");
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", setupFailureCount: 1, setupFailure: setupFailure);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        FunctionMetadata firstFunction = CreateFunction("FirstFunction");
        await registry.ActivateAsync([firstFunction], new ManagedDependencyOptions(), functionTimeout: null);

        WorkerChannel linkedChannel = await LinkAsync(registry, channel.Id);
        await channel.SetupEntered.WaitAsync(TestTimeout);

        Assert.Same(channel.Channel, linkedChannel);
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);
        Assert.False(registry.GetReadyChannelsAsync().IsCompleted);
        Assert.Equal(1, channel.SetupAttempts);

        FunctionMetadata secondFunction = CreateFunction("SecondFunction");
        await registry.ActivateAsync([secondFunction], new ManagedDependencyOptions(), functionTimeout: null);
        StreamingMessage request = await channel.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);

        Assert.Equal(secondFunction.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
        Assert.Equal(2, channel.SetupAttempts);
        Assert.Same(channel.Channel, Assert.Single((await registry.GetReadyChannelsAsync()).ToArray()));
    }

    [Fact]
    public async Task ActivateAsync_CanceledWhileWaitingForStateLockPreservesContext()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockSetup: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, channel.Id);
        FunctionMetadata activeFunction = CreateFunction("ActiveFunction");
        FunctionMetadata replacementFunction = CreateFunction("ReplacementFunction");
        using CancellationTokenSource cancellation = new();
        Task activation = Task.Run(() => registry.ActivateAsync([activeFunction], new ManagedDependencyOptions(), functionTimeout: null));
        Task canceledActivation = null;
        Task<IReadOnlyList<WorkerChannel>> readiness = registry.GetReadyChannelsAsync().AsTask();
        try
        {
            await channel.SetupEntered.WaitAsync(TestTimeout);
            TaskCompletionSource<Thread> activationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            canceledActivation = Task.Run(() =>
            {
                activationStarted.SetResult(Thread.CurrentThread);
                return registry.ActivateAsync([replacementFunction], new ManagedDependencyOptions(), functionTimeout: null, cancellation.Token);
            });
            Thread activationThread = await activationStarted.Task.WaitAsync(TestTimeout);
            await WaitUntilAsync(() => activationThread.ThreadState.HasFlag(ThreadState.WaitSleepJoin));
            cancellation.Cancel();
        }
        finally
        {
            channel.AllowSetup();
            await activation.WaitAsync(TestTimeout);
        }

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledActivation.WaitAsync(TestTimeout));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        IReadOnlyList<WorkerChannel> readyChannels = await readiness.WaitAsync(TestTimeout);
        Assert.Same(readyChannels, await registry.GetReadyChannelsAsync());
        Assert.Same(channel.Channel, Assert.Single(readyChannels));
        Assert.Equal(1, channel.SetupAttempts);
        Assert.DoesNotContain(replacementFunction.GetFunctionId(), channel.Channel.FunctionInputBuffers.Keys);
        StreamingMessage request = await channel.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        Assert.Equal(activeFunction.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
        Assert.False(channel.Transport.Requests.TryRead(out _));
    }

    [Fact]
    public async Task ActivateAsync_ReconfiguresChannelsForReplacementContext()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, channel.Id);
        FunctionMetadata firstFunction = CreateFunction("FirstFunction");
        FunctionMetadata secondFunction = CreateFunction("SecondFunction");

        await registry.ActivateAsync([firstFunction], new ManagedDependencyOptions(), functionTimeout: null);
        StreamingMessage firstRequest = await channel.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await registry.ActivateAsync([secondFunction], new ManagedDependencyOptions(), functionTimeout: null);
        StreamingMessage secondRequest = await channel.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);

        Assert.Equal(firstFunction.GetFunctionId(), firstRequest.FunctionLoadRequest.FunctionId);
        Assert.Equal(secondFunction.GetFunctionId(), secondRequest.FunctionLoadRequest.FunctionId);
        Assert.Equal(2, channel.SetupAttempts);
        Assert.Same(channel.Channel, Assert.Single((await registry.GetReadyChannelsAsync()).ToArray()));
    }

    [Fact]
    public async Task ActivateAsync_RestartGapLateLinkIsReconfiguredForReplacementContext()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl late = new("late", blockSetup: true);
        harness.Enqueue(first);
        harness.Enqueue(late);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, first.Id);
        FunctionMetadata firstFunction = CreateFunction("FirstFunction");
        FunctionMetadata replacementFunction = CreateFunction("ReplacementFunction");
        await registry.ActivateAsync([firstFunction], new ManagedDependencyOptions(), functionTimeout: null);
        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);

        Task<WorkerChannel> lateLink = LinkAsync(registry, late.Id);
        Task replacementActivation;
        try
        {
            await late.SetupEntered.WaitAsync(TestTimeout);
            Assert.Same(late.Channel, await lateLink.WaitAsync(TestTimeout));
            replacementActivation = Task.Run(() => registry.ActivateAsync(
                [replacementFunction],
                new ManagedDependencyOptions(),
                functionTimeout: null));
        }
        finally
        {
            late.AllowSetup();
        }

        StreamingMessage lateFirstRequest = await late.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await replacementActivation.WaitAsync(TestTimeout);
        StreamingMessage lateReplacementRequest = await late.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        StreamingMessage firstReplacementRequest = await first.ReadRequestAsync(
            StreamingMessage.ContentOneofCase.FunctionLoadRequest);

        Assert.Equal(firstFunction.GetFunctionId(), lateFirstRequest.FunctionLoadRequest.FunctionId);
        Assert.Equal(replacementFunction.GetFunctionId(), lateReplacementRequest.FunctionLoadRequest.FunctionId);
        Assert.Equal(replacementFunction.GetFunctionId(), firstReplacementRequest.FunctionLoadRequest.FunctionId);
        IReadOnlyList<WorkerChannel> readyChannels = (await registry.GetReadyChannelsAsync()).ToArray();
        Assert.Equal(2, readyChannels.Count);
        Assert.Contains(first.Channel, readyChannels);
        Assert.Contains(late.Channel, readyChannels);
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
    public async Task DisposeAsync_WhileConnectionIsPending_WaitsForOperationCleanup()
    {
        RegistryHarness harness = new();
        TaskCompletionSource<DuplexChannel<StreamingMessage>> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.DuplexFactory.Setup(factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(connection.Task);
        await using WorkerChannelRegistry registry = harness.Registry;
        Task<WorkerChannel> link = LinkAsync(registry, "worker");
        Task dispose = registry.DisposeAsync().AsTask();

        try
        {
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            connection.TrySetCanceled();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => link.WaitAsync(TestTimeout));
            await dispose.WaitAsync(TestTimeout);
        }

        harness.DuplexFactory.Verify(
            factory => factory.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.ChannelFactory.VerifyNoOtherCalls();
        Assert.Empty(registry.GetInitializedChannels());
    }

    [Fact]
    public async Task DisposeAsync_ReleasesInitializedAndReadyWaiters()
    {
        RegistryHarness harness = new();
        WorkerChannelRegistry registry = harness.Registry;
        await registry.ActivateAsync([], new ManagedDependencyOptions(), functionTimeout: null);
        Task<WorkerChannel> initializedWaiter = registry.WaitForFirstInitializedAsync();
        Task<IReadOnlyList<WorkerChannel>> readyWaiter = registry.GetReadyChannelsAsync().AsTask();

        await registry.DisposeAsync();

        ObjectDisposedException initializedWaiterException =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => initializedWaiter.WaitAsync(TestTimeout));
        ObjectDisposedException readyWaiterException =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => readyWaiter.WaitAsync(TestTimeout));
        ObjectDisposedException newInitializedWaitException =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.WaitForFirstInitializedAsync());
        ObjectDisposedException newReadyWaitException =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.GetReadyChannelsAsync().AsTask());
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, initializedWaiterException.ObjectName);
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, readyWaiterException.ObjectName);
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, newInitializedWaitException.ObjectName);
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, newReadyWaitException.ObjectName);
    }

    [Fact]
    public async Task DisposeAsync_InvalidatesPublishedReadySnapshot()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker");
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, channel.Id);
        await registry.ActivateAsync([], new ManagedDependencyOptions(), functionTimeout: null);
        Assert.True(registry.GetReadyChannelsAsync().IsCompletedSuccessfully);

        await registry.DisposeAsync();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => registry.GetReadyChannelsAsync().AsTask());
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, exception.ObjectName);
    }

    private static async Task<WorkerChannel> LinkAsync(WorkerChannelRegistry registry, string workerId)
        => (await registry.LinkAsync(workerId, CreateEndpoint(workerId)).ConfigureAwait(false)).Channel;

    private static Uri CreateEndpoint(string workerId)
        => new($"http://{workerId}.test:5000");

    private static FunctionMetadata CreateFunction(string name)
        => new()
        {
            Language = "external",
            Name = name,
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeoutSource = new(TestTimeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeoutSource.Token);
        }
    }

    private static RpcClientWorkerChannelFactory CreateRealChannelFactory(
        IMetricsLogger metricsLogger = null,
        IHttpProxyService httpProxyService = null)
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
            httpProxyService ?? Mock.Of<IHttpProxyService>(),
            metricsLogger ?? Mock.Of<IMetricsLogger>());
    }

    private sealed class RegistryHarness
    {
        private readonly ConcurrentDictionary<Uri, ConcurrentQueue<ChannelControl>> _channels = new();
        private readonly ConcurrentDictionary<DuplexChannel<StreamingMessage>, ChannelControl> _controls = new();
        private readonly ConcurrentQueue<TestDuplexChannel<StreamingMessage>> _transports = new();

        public RegistryHarness()
            : this(null, TimeProvider.System)
        {
        }

        public RegistryHarness(TimeSpan? linkTimeout)
            : this(linkTimeout, TimeProvider.System)
        {
        }

        public RegistryHarness(TimeProvider timeProvider)
            : this(null, timeProvider)
        {
        }

        private RegistryHarness(TimeSpan? linkTimeout, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);

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

                    RpcClientWorkerChannel channel = CreateRealChannelFactory(
                        control.MetricsLogger.Object,
                        control.HttpProxyService)
                        .Create(control.Id, ownedChannel);
                    control.Attach(channel);

                    return channel;
                });
            Registry = linkTimeout.HasValue
                ? new(
                    DuplexFactory.Object,
                    ChannelFactory.Object,
                    NullLogger<WorkerChannelRegistry>.Instance,
                    linkTimeout.Value,
                    timeProvider)
                : new(
                    DuplexFactory.Object,
                    ChannelFactory.Object,
                    NullLogger<WorkerChannelRegistry>.Instance,
                    timeProvider);
        }

        internal Mock<IRpcClientWorkerChannelFactory> ChannelFactory { get; } = new();

        internal Mock<IDuplexChannelFactory<StreamingMessage>> DuplexFactory { get; } = new();

        internal WorkerChannelRegistry Registry { get; }

        internal IReadOnlyCollection<TestDuplexChannel<StreamingMessage>> Transports => _transports.ToArray();

        internal void Enqueue(ChannelControl channel)
            => Enqueue(channel.Id, channel);

        internal void Enqueue(string workerId, ChannelControl channel)
            => Enqueue(CreateEndpoint(workerId), channel);

        internal void Enqueue(Uri endpoint, ChannelControl channel)
        {
            ConcurrentQueue<ChannelControl> channels = _channels.GetOrAdd(endpoint, static _ => new());
            channels.Enqueue(channel);
        }
    }

    private sealed class ChannelControl
    {
        private readonly TaskCompletionSource _startEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _startRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _setupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _setupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _advertisedHttpUri;
        private readonly bool _completeResponsesOnRequestCompletion;
        private readonly string _functionGroupName;
        private readonly string _isAlwaysReady;
        private readonly string _maxHttpConcurrency;
        private readonly Exception _setupFailure;
        private readonly bool _trustedAssignmentMetadata;
        private int _remainingSetupFailures;
        private int _setupAttempts;
        private Exception _startFailure;
        private RpcClientWorkerChannel _channel;

        internal ChannelControl(
            string id,
            bool blockStart = false,
            bool blockDisposal = false,
            bool blockSetup = false,
            int setupFailureCount = 0,
            Exception setupFailure = null,
            string functionGroupName = null,
            string isAlwaysReady = null,
            string maxHttpConcurrency = null,
            IHttpProxyService httpProxyService = null,
            bool completeResponsesOnRequestCompletion = false,
            string advertisedHttpUri = null,
            bool trustedAssignmentMetadata = true)
        {
            Id = id;
            Transport = new(blockDisposal);
            HttpProxyService = httpProxyService ?? Mock.Of<IHttpProxyService>();
            _advertisedHttpUri = advertisedHttpUri;
            _functionGroupName = functionGroupName;
            _isAlwaysReady = isAlwaysReady;
            _maxHttpConcurrency = maxHttpConcurrency;
            _trustedAssignmentMetadata = trustedAssignmentMetadata;
            _completeResponsesOnRequestCompletion = completeResponsesOnRequestCompletion;
            _remainingSetupFailures = setupFailureCount;
            _setupFailure = setupFailure ?? new InvalidOperationException("Channel setup failed.");
            if (!blockStart)
            {
                _startRelease.TrySetResult();
            }

            if (!blockSetup)
            {
                _setupRelease.TrySetResult();
            }

            MetricsLogger.Setup(logger => logger.BeginEvent(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns((string eventName, string _, string _) =>
                {
                    if (!string.Equals(
                        eventName,
                        MetricEventNames.FunctionLoadRequestResponse,
                        StringComparison.Ordinal))
                    {
                        return null;
                    }

                    Interlocked.Increment(ref _setupAttempts);
                    _setupEntered.TrySetResult();
                    _setupRelease.Task.GetAwaiter().GetResult();
                    if (Interlocked.Decrement(ref _remainingSetupFailures) >= 0)
                    {
                        throw _setupFailure;
                    }

                    return null;
                });
        }

        internal RpcClientWorkerChannel Channel
            => Interlocked.CompareExchange(ref _channel, null, null);

        internal int DisposeCount => Transport.DisposeCount;

        internal Task DisposeStarted => Transport.DisposeStarted;

        internal string Id { get; }

        internal IHttpProxyService HttpProxyService { get; }

        internal Mock<IMetricsLogger> MetricsLogger { get; } = new();

        internal int SetupAttempts => Interlocked.CompareExchange(ref _setupAttempts, 0, 0);

        internal Task SetupEntered => _setupEntered.Task;

        internal Task StartEntered => _startEntered.Task;

        internal TestDuplexChannel<StreamingMessage> Transport { get; }

        internal void AllowDispose() => Transport.AllowDispose();

        internal void AllowSetup() => _setupRelease.TrySetResult();

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

        internal async Task<Exception> FailFunctionLoadAsync()
        {
            await Transport.SendResponseAsync(new()
            {
                FunctionLoadResponse = new()
                {
                    FunctionId = "unknown-function",
                    Result = new() { Status = StatusResult.Types.Status.Success },
                },
            });

            return await Channel.FunctionLoadFailure.WaitAsync(TestTimeout);
        }

        internal async Task<StreamingMessage> ReadRequestAsync(
            StreamingMessage.ContentOneofCase contentCase)
        {
            using CancellationTokenSource timeout = new(TestTimeout);
            while (true)
            {
                StreamingMessage request = await Transport.Requests.ReadAsync(timeout.Token);
                if (request.ContentCase == contentCase)
                {
                    return request;
                }
            }
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

                WorkerInitResponse initResponse = new()
                {
                    Result = new() { Status = StatusResult.Types.Status.Success },
                };
                if (_trustedAssignmentMetadata && _functionGroupName is not null)
                {
                    initResponse.Capabilities[RpcClientWorkerChannel.WorkerAssignmentMetadataVersionCapability] =
                        RpcClientWorkerChannel.WorkerAssignmentMetadataVersion;
                }

                if (_functionGroupName is not null)
                {
                    initResponse.Capabilities[RpcClientWorkerChannel.FunctionGroupNameCapability] = _functionGroupName;
                }

                if (_isAlwaysReady is not null)
                {
                    initResponse.Capabilities[RpcClientWorkerChannel.IsAlwaysReadyCapability] = _isAlwaysReady;
                }

                if (_maxHttpConcurrency is not null)
                {
                    initResponse.Capabilities[RpcClientWorkerChannel.MaxHttpConcurrencyCapability] = _maxHttpConcurrency;
                }

                if (_advertisedHttpUri is not null)
                {
                    initResponse.Capabilities[RpcWorkerConstants.HttpUri] = _advertisedHttpUri;
                }

                await Transport.SendResponseAsync(new() { WorkerInitResponse = initResponse });

                if (_completeResponsesOnRequestCompletion)
                {
                    await Transport.Requests.Completion;
                    Transport.CompleteResponses();
                }
            }
            catch (ChannelClosedException)
            {
                // Transport teardown can win before a deliberately blocked test handshake completes.
            }
        }
    }
}
