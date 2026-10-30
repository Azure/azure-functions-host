// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
    public async Task LinkAsync_GrpcInvalidNegotiatedHttpOrigin_FailsInitializationAndAllowsRetry()
    {
        using var timeout = new CancellationTokenSource(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddWorker(GrpcWorkerId, httpUri: "not-an-absolute-uri");
        await using RpcClientFactory clientFactory = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clientFactory);

        WorkerLinkException failure = await Assert.ThrowsAsync<WorkerLinkException>(
            () => registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token));
        Assert.Equal(WorkerLinkFailureReason.Unavailable, failure.Reason);

        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);
        server.AddWorker(GrpcWorkerId, httpUri: "https://worker-proxy:28080");
        WorkerLinkResult linked = await registry.LinkAsync(GrpcWorkerId, server.Endpoint, RuntimeWorkerTestData.WorkerKey, timeout.Token);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkAsync_DifferentCredentialOrGrpcOriginConflictsWithoutReinitialization(bool changeKey)
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;
        Uri grpcEndpoint = CreateEndpoint("worker");
        string differentKey = Convert.ToBase64String(SHA256.HashData("different-worker-key"u8));
        Task<WorkerLinkResult> first = registry.LinkAsync("worker", grpcEndpoint, RuntimeWorkerTestData.WorkerKey);
        await channel.StartEntered.WaitAsync(TestTimeout);

        foreach (bool ready in new[] { false, true })
        {
            if (ready)
            {
                channel.AllowStart();
                await first.WaitAsync(TestTimeout);
            }

            WorkerLinkException failure = await Assert.ThrowsAsync<WorkerLinkException>(() =>
                registry.LinkAsync("worker", changeKey ? grpcEndpoint : new Uri("https://different-proxy:48802"),
                    changeKey ? differentKey : RuntimeWorkerTestData.WorkerKey));
            Assert.Equal(WorkerLinkFailureReason.Conflict, failure.Reason);
            Assert.DoesNotContain(differentKey, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(RuntimeWorkerTestData.WorkerKey, failure.Message, StringComparison.Ordinal);
        }

        Assert.Single(harness.Transports);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("wrong-key")]
    [InlineData("wrong-app")]
    [InlineData("wrong-worker")]
    [InlineData("worker-case")]
    [InlineData("missing-worker")]
    [InlineData("wrong-purpose")]
    [InlineData("caller-token")]
    public async Task LinkAsync_InvalidReverseProofNeverPublishesAndCleansTransport(string failure)
    {
        using CancellationTokenSource timeout = new(GrpcTestTimeout);
        await using FakeWorkerProxyGrpcServer server = await FakeWorkerProxyGrpcServer.StartAsync(timeout.Token);
        var worker = server.AddPendingWorker(GrpcWorkerId);
        await using RpcClientFactory clients = new(NullLogger<RpcClientFactory>.Instance);
        await using WorkerChannelRegistry registry = CreateGrpcRegistry(clients);
        Task<WorkerLinkResult> link = registry.LinkAsync(GrpcWorkerId, server.Endpoint, timeout.Token);
        await worker.InitializationStarted.WaitAsync(timeout.Token);
        worker.CompleteInitialization(appProof: InvalidAppProof(failure),
            includeProof: !string.Equals(failure, "missing", StringComparison.Ordinal));

        WorkerLinkException error = await Assert.ThrowsAsync<WorkerLinkException>(() => link.WaitAsync(timeout.Token));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, error.Reason);
        Assert.Empty(registry.GetInitializedChannels());
        await worker.Disconnected.WaitAsync(timeout.Token);
        Assert.DoesNotContain(RuntimeWorkerTestData.AppKey, error.Message, StringComparison.Ordinal);
    }

    private static string InvalidAppProof(string failure)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        byte[] key = Convert.FromHexString(RuntimeWorkerTestData.AppKey);
        string audience = RuntimeWorkerAuthentication.AppAudience("test-app");
        string purpose = RuntimeWorkerAuthentication.AppPurpose;

        return failure switch
        {
            "empty" => string.Empty,
            "malformed" => "invalid-proof",
            "tampered" => RuntimeWorkerTestData.CreateAppProof(GrpcWorkerId) + "A",
            "expired" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(-6), GrpcWorkerId),
            "future" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now.AddMinutes(1), GrpcWorkerId),
            "wrong-key" => RuntimeWorkerAuthentication.CreateToken(new byte[32], audience, purpose, now, GrpcWorkerId),
            "wrong-app" => RuntimeWorkerAuthentication.CreateToken(key, audience + "-other-app", purpose, now, GrpcWorkerId),
            "wrong-worker" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now, "different-worker"),
            "worker-case" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now, GrpcWorkerId.ToUpperInvariant()),
            "missing-worker" => RuntimeWorkerAuthentication.CreateToken(key, audience, purpose, now),
            "wrong-purpose" => RuntimeWorkerAuthentication.CreateToken(key, audience, "wrong-purpose", now, GrpcWorkerId),
            "caller-token" => RuntimeWorkerTestData.CreateCredentials(GrpcWorkerId).CreateHeaders()[RuntimeWorkerAuthentication.WorkerTokenHeader],
            _ => RuntimeWorkerTestData.CreateAppProof(GrpcWorkerId)
        };
    }

    [Theory]
    [InlineData(null, null, 75)]
    [InlineData("00:02:30", "00:00:20", 175)]
    public async Task LinkBudgetAndChannelTimeouts_UseTheSameConfiguredWorkerOptions(
        string? startup, string? initialization, int totalSeconds)
    {
        ServiceCollection services = new();
        Dictionary<string, string?> values = new();
        if (startup is not null)
        {
            values["RpcClient:Worker:ProcessStartupTimeout"] = startup;
            values["RpcClient:Worker:InitializationTimeout"] = initialization;
        }
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddRpcClientServices();
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<WorkerProcessCountOptions> options = provider.GetRequiredService<IOptionsMonitor<WorkerProcessCountOptions>>();
        WorkerProcessCountOptions expected = options.Get(RpcClientWorkerChannelFactory.WorkerOptionsName);
        await using WorkerChannelRegistry registry = new(Mock.Of<IDuplexChannelFactory<StreamingMessage>>(),
            Mock.Of<IRpcClientWorkerChannelFactory>(), NullLogger<WorkerChannelRegistry>.Instance,
            RuntimeWorkerTestData.CreateConfiguration(), TimeProvider.System, Mock.Of<IWorkerHostConfigurationParser>(), options);
        await using RpcClientWorkerChannel channel = CreateRealChannelFactory(processOptions: options)
            .Create("worker", new TestDuplexChannel<StreamingMessage>(), RuntimeWorkerTestData.CreateCredentials("worker"));

        Assert.Equal(TimeSpan.FromSeconds(totalSeconds), registry.LinkTimeout);
        Assert.Equal(expected.ProcessStartupTimeout, channel.WorkerConfig.CountOptions.ProcessStartupTimeout);
        Assert.Equal(expected.InitializationTimeout, channel.WorkerConfig.CountOptions.InitializationTimeout);
        Assert.Equal(new WorkerProcessCountOptions().EnvironmentReloadTimeout, channel.WorkerConfig.CountOptions.EnvironmentReloadTimeout);
        Assert.Equal(RpcClientFactory.DefaultConnectTimeout + channel.WorkerConfig.CountOptions.ProcessStartupTimeout
            + channel.WorkerConfig.CountOptions.InitializationTimeout, registry.LinkTimeout);
        await using WorkerChannelRegistry identityRegistry = new(Mock.Of<IDuplexChannelFactory<StreamingMessage>>(),
            Mock.Of<IRpcClientWorkerChannelFactory>(), NullLogger<WorkerChannelRegistry>.Instance,
            RuntimeWorkerTestData.CreateConfiguration(), TimeProvider.System, Mock.Of<IWorkerHostConfigurationParser>(), options,
            Options.Create(new TokenServiceProxyOptions { Enabled = true }));
        Assert.Equal(TimeSpan.FromMinutes(2), identityRegistry.LinkTimeout);
    }

    [Theory]
    [InlineData("ProcessStartupTimeout", "00:00:00")]
    [InlineData("InitializationTimeout", "-00:00:01")]
    [InlineData("ProcessStartupTimeout", "100.00:00:00")]
    public void WorkerOptions_RejectUnboundedOrNonpositiveTimeouts(string setting, string value)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"RpcClient:Worker:{setting}"] = value
        }).Build());
        services.AddRpcClientServices();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptionsMonitor<WorkerProcessCountOptions>>().Get(RpcClientWorkerChannelFactory.WorkerOptionsName));
    }

    private static WorkerChannelRegistry CreateGrpcRegistry(RpcClientFactory clientFactory, TimeSpan? linkTimeout = null)
    {
        FunctionRpcDuplexChannelFactory duplexFactory = new(clientFactory, NullLogger<FunctionRpcDuplexChannelFactory>.Instance);
        RpcClientWorkerChannelFactory channelFactory = CreateRealChannelFactory();
        WorkerHostConfigurationParser workerHostConfigurationParser = new(
            new ConfigurationBuilder().Build(), NullLogger<WorkerHostConfigurationParser>.Instance);

        return linkTimeout.HasValue
            ? new(duplexFactory, channelFactory, NullLogger<WorkerChannelRegistry>.Instance,
                RuntimeWorkerTestData.CreateConfiguration(), TimeProvider.System, linkTimeout.Value, workerHostConfigurationParser)
            : new(duplexFactory, channelFactory, NullLogger<WorkerChannelRegistry>.Instance,
                RuntimeWorkerTestData.CreateConfiguration(), TimeProvider.System, workerHostConfigurationParser);
    }
}
