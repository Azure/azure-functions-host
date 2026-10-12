// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed partial class WorkerChannelRegistryTests
{
    private const string GrpcWorkerId = "worker-pod-abc123";
    private static readonly TimeSpan GrpcTestTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public async Task LinkAsync_GrpcHandshake_RegistersOnlyAfterInitializationAndSharesRetries(int retryCount)
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        Task<WorkerLinkResult> original = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        Task<WorkerLinkResult>[] retries = [.. Enumerable.Range(0, retryCount)
            .Select(_ => registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token))];

        Assert.False(original.IsCompleted);
        Assert.All(retries, retry => Assert.False(retry.IsCompleted));
        Assert.Empty(registry.GetInitializedChannels());
        Assert.Equal(1, server.StreamCount);

        worker.CompleteInitialization();
        WorkerLinkResult linked = await original.WaitAsync(timeout.Token);
        Assert.True(linked.IsNewLink);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        foreach (WorkerLinkResult replay in await Task.WhenAll(retries).WaitAsync(timeout.Token))
        {
            Assert.False(replay.IsNewLink);
            Assert.Same(linked.Channel, replay.Channel);
        }

        WorkerLinkResult completedReplay = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.False(completedReplay.IsNewLink);
        Assert.Same(linked.Channel, completedReplay.Channel);
        Assert.Equal(1, server.StreamCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_GrpcDifferentWorkers_InitializeIndependently(bool shareEndpoint)
    {
        const string secondWorkerId = "worker-pod-def456";
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer firstServer = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using FakeWorkerProxyGrpcServer otherEndpoint = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        FakeWorkerProxyGrpcServer secondServer = shareEndpoint ? firstServer : otherEndpoint;
        var firstWorker = firstServer.AddPendingWorker(GrpcWorkerId);
        var secondWorker = secondServer.AddPendingWorker(secondWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        Task<WorkerLinkResult> first = registry.LinkAsync(GrpcWorkerId, firstServer.Endpoint, timeout.Token);
        await firstWorker.InitializationStarted.WaitAsync(timeout.Token);
        Task<WorkerLinkResult> second = registry.LinkAsync(secondWorkerId, secondServer.Endpoint, timeout.Token);
        await secondWorker.InitializationStarted.WaitAsync(timeout.Token);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Empty(registry.GetInitializedChannels());

        secondWorker.CompleteInitialization();
        WorkerLinkResult secondLinked = await second.WaitAsync(timeout.Token);
        Assert.True(secondLinked.IsNewLink);
        Assert.Same(secondLinked.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.False(first.IsCompleted);

        firstWorker.CompleteInitialization();
        WorkerLinkResult firstLinked = await first.WaitAsync(timeout.Token);
        Assert.True(firstLinked.IsNewLink);
        Assert.Equal(2, registry.GetInitializedChannels().Count);
        WorkerLinkResult firstReplay = await registry.LinkAsync(GrpcWorkerId, firstServer.Endpoint, timeout.Token);
        WorkerLinkResult secondReplay = await registry.LinkAsync(secondWorkerId, secondServer.Endpoint, timeout.Token);
        Assert.False(firstReplay.IsNewLink);
        Assert.False(secondReplay.IsNewLink);
        Assert.Same(firstLinked.Channel, firstReplay.Channel);
        Assert.Same(secondLinked.Channel, secondReplay.Channel);
        Assert.Equal(shareEndpoint ? 2 : 1, firstServer.StreamCount);
        Assert.Equal(shareEndpoint ? 0 : 1, otherEndpoint.StreamCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_GrpcRejectedInitialization_FailsWaitersAndAllowsRetry(bool concurrentRetry)
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        Task<WorkerLinkResult> original = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        Task<WorkerLinkResult>? retry = concurrentRetry
            ? registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token)
            : null;
        worker.FailInitialization("Worker initialization failed.");

        await Assert.ThrowsAsync<WorkerRpcException>(() => original.WaitAsync(timeout.Token));
        if (retry is not null)
        {
            await Assert.ThrowsAsync<WorkerRpcException>(() => retry.WaitAsync(timeout.Token));
        }

        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);
        Assert.Equal(1, server.StreamCount);

        server.AddWorker(GrpcWorkerId);
        WorkerLinkResult linked = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.True(linked.IsNewLink);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(2, server.StreamCount);
    }

    [Fact]
    public async Task LinkAsync_GrpcInvalidHttpUri_FailsInitializationAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddWorker(GrpcWorkerId, httpUri: "not-an-absolute-uri");
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        await Assert.ThrowsAsync<UriFormatException>(() => registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token));

        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);
        server.AddWorker(GrpcWorkerId, httpUri: "http://worker-proxy:28080");
        WorkerLinkResult linked = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.True(linked.IsNewLink);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(2, server.StreamCount);
    }

    [Fact]
    public async Task LinkAsync_GrpcConflictingEndpoint_IsRejectedWithoutDialing()
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        await using FakeWorkerProxyGrpcServer otherEndpoint = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        Task<WorkerLinkResult> original = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);

        WorkerLinkException pendingConflict = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync(GrpcWorkerId, otherEndpoint.Endpoint, timeout.Token));
        Assert.Equal(WorkerLinkFailureReason.Conflict, pendingConflict.Reason);
        Assert.False(original.IsCompleted);
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);
        Assert.Equal(1, clientFactory.CachedChannelCount);

        worker.CompleteInitialization();
        WorkerLinkResult linked = await original.WaitAsync(timeout.Token);
        WorkerLinkException readyConflict = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync(GrpcWorkerId, otherEndpoint.Endpoint, timeout.Token));
        Assert.Equal(WorkerLinkFailureReason.Conflict, readyConflict.Reason);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(1, server.StreamCount);
        Assert.Equal(0, otherEndpoint.StreamCount);
        Assert.Equal(1, clientFactory.CachedChannelCount);
    }

    [Fact]
    public async Task LinkAsync_GrpcCanceledInitialization_CleansAttemptAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        Task<WorkerLinkResult> original = registry.LinkAsync(GrpcWorkerId, server.Endpoint, cancellation.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original.WaitAsync(timeout.Token));
        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);

        server.AddWorker(GrpcWorkerId);
        WorkerLinkResult linked = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.True(linked.IsNewLink);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(2, server.StreamCount);
    }

    [Fact]
    public async Task LinkAsync_GrpcCanceledRetry_DoesNotCancelOriginalInitialization()
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        Task<WorkerLinkResult> original = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        Task<WorkerLinkResult> retry = registry.LinkAsync(GrpcWorkerId, server.Endpoint, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.WaitAsync(timeout.Token));
        Assert.False(original.IsCompleted);
        Assert.False(worker.Disconnected.IsCompleted);

        worker.CompleteInitialization();
        WorkerLinkResult linked = await original.WaitAsync(timeout.Token);
        Assert.True(linked.IsNewLink);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        Assert.Equal(1, server.StreamCount);
    }

    [Fact]
    public async Task LinkAsync_GrpcInitializationDeadline_FailsWaitersAndAllowsRetryAfterCleanup()
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory, TimeSpan.FromSeconds(2));

        Task<WorkerLinkResult> original = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        Task<WorkerLinkResult> retry = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.False(original.IsCompleted);
        Assert.False(retry.IsCompleted);

        await Assert.ThrowsAsync<TimeoutException>(() => original.WaitAsync(timeout.Token));
        await Assert.ThrowsAsync<TimeoutException>(() => retry.WaitAsync(timeout.Token));
        Assert.False(timeout.IsCancellationRequested);
        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);
        Assert.Equal(1, server.StreamCount);

        server.AddWorker(GrpcWorkerId);
        WorkerLinkResult linked = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.True(linked.IsNewLink);
        Assert.Same(linked.Channel, Assert.Single(registry.GetInitializedChannels()));
        WorkerLinkResult replay = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        Assert.False(replay.IsNewLink);
        Assert.Same(linked.Channel, replay.Channel);
        Assert.Equal(2, server.StreamCount);
    }

    private static WorkerChannelRegistry CreateGrpcRegistry(RpcClientFactory clientFactory, TimeSpan? linkTimeout = null)
    {
        FunctionRpcDuplexChannelFactory duplexFactory = new(clientFactory, NullLogger<FunctionRpcDuplexChannelFactory>.Instance);
        RpcClientWorkerChannelFactory channelFactory = CreateRealChannelFactory();

        return linkTimeout.HasValue
            ? new(duplexFactory, channelFactory, NullLogger<WorkerChannelRegistry>.Instance, linkTimeout.Value)
            : new(duplexFactory, channelFactory, NullLogger<WorkerChannelRegistry>.Instance);
    }
}
