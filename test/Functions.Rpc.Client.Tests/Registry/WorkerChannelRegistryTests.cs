// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    [Fact]
    public async Task LinkAsync_DifferentWorkersInitializeConcurrently()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first", blockStart: true);
        ChannelControl second = new("second", blockStart: true);
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;

        Task<WorkerChannel> firstLink = LinkAsync(registry, "first");
        Task<WorkerChannel> secondLink = LinkAsync(registry, "second");

        await Task.WhenAll(first.StartEntered, second.StartEntered).WaitAsync(TestTimeout);
        first.AllowStart();
        second.AllowStart();

        await Task.WhenAll(firstLink, secondLink).WaitAsync(TestTimeout);
        Assert.Equal(2, registry.GetInitializedChannels().Count);
    }

    [Fact]
    public async Task LinkAsync_SameWorkerRejectsDuplicateWhileFirstLinkIsPending()
    {
        RegistryHarness harness = new();
        ChannelControl channel = new("worker", blockStart: true);
        harness.Enqueue(channel);
        await using WorkerChannelRegistry registry = harness.Registry;

        Task<WorkerChannel> firstLink = LinkAsync(registry, "worker");
        await channel.StartEntered.WaitAsync(TestTimeout);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LinkAsync(registry, "worker"));
        Assert.Equal("Worker 'worker' is already linked.", exception.Message);
        harness.ChannelFactory.Verify(factory => factory.Create("worker", It.IsAny<DuplexChannel<StreamingMessage>>()), Times.Once);
        channel.AllowStart();
        await firstLink.WaitAsync(TestTimeout);
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
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LinkAsync(registry, "worker"));
        Assert.Equal("Worker 'worker' is already linked.", exception.Message);

        channel.AllowStart();
        WorkerChannel linkedChannel = await link.WaitAsync(TestTimeout);
        Assert.Same(channel.Channel, linkedChannel);
        Assert.True(registry.TryGetInitializedChannel("worker", out WorkerChannel initializedChannel));
        Assert.Same(channel.Channel, initializedChannel);
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
        InvalidOperationException expected = new("initialization failed");

        Task<WorkerChannel> failedLink = LinkAsync(registry, "worker");
        await failedChannel.StartEntered.WaitAsync(TestTimeout);
        failedChannel.FailStart(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
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
    public async Task UnlinkAsync_IsIdempotentAndAllowsRelink()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("worker");
        ChannelControl second = new("worker");
        harness.Enqueue(first);
        harness.Enqueue(second);
        await using WorkerChannelRegistry registry = harness.Registry;

        await LinkAsync(registry, "worker");

        Assert.True(await registry.UnlinkAsync("worker"));
        Assert.False(await registry.UnlinkAsync("worker"));
        Assert.Equal(1, first.DisposeCount);
        WorkerChannel replacement = await LinkAsync(registry, "worker");
        Assert.Same(second.Channel, replacement);
    }

    [Fact]
    public async Task Completion_RemovesOnlyTerminalWorkerAndAllowsRelink()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("first");
        ChannelControl second = new("second");
        ChannelControl replacement = new("first");
        harness.Enqueue(first);
        harness.Enqueue(second);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;

        await LinkAsync(registry, "first");
        await LinkAsync(registry, "second");
        first.Complete(new InvalidOperationException("transport failed"));
        await first.DisposeStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => !registry.TryGetInitializedChannel("first", out _));

        Assert.False(registry.TryGetInitializedChannel("first", out _));
        Assert.True(registry.TryGetInitializedChannel("second", out WorkerChannel healthyChannel));
        Assert.Same(second.Channel, healthyChannel);
        WorkerChannel relinkedChannel = await LinkAsync(registry, "first");
        Assert.Same(replacement.Channel, relinkedChannel);
    }

    [Fact]
    public async Task UnlinkAsync_RejectsRelinkUntilCleanupCompletes()
    {
        RegistryHarness harness = new();
        ChannelControl first = new("worker", blockDisposal: true);
        ChannelControl replacement = new("worker");
        harness.Enqueue(first);
        harness.Enqueue(replacement);
        await using WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, "worker");

        Task<bool> unlink = registry.UnlinkAsync("worker");
        await first.DisposeStarted.WaitAsync(TestTimeout);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LinkAsync(registry, "worker"));
        Assert.Equal("Worker 'worker' is already linked.", exception.Message);

        first.AllowDispose();
        Assert.True(await unlink.WaitAsync(TestTimeout));
        WorkerChannel relinkedChannel = await LinkAsync(registry, "worker").WaitAsync(TestTimeout);
        Assert.Same(replacement.Channel, relinkedChannel);
        Assert.True(registry.TryGetInitializedChannel("worker", out WorkerChannel initializedChannel));
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
        ChannelControl last = new("z-worker");
        ChannelControl first = new("a-worker");
        ChannelControl middle = new("m-worker");
        harness.Enqueue(last);
        harness.Enqueue(first);
        harness.Enqueue(middle);
        await using WorkerChannelRegistry registry = harness.Registry;

        await LinkAsync(registry, last.Id);
        await LinkAsync(registry, first.Id);
        await LinkAsync(registry, middle.Id);

        IReadOnlyList<WorkerChannel> channels = registry.GetInitializedChannels();

        Assert.Equal(["a-worker", "m-worker", "z-worker"],
            channels.Select(channel => channel.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.NotSame(channels, registry.GetInitializedChannels());
    }

    [Fact]
    public async Task DisposeAsync_CancelsLinksAndDisposesEveryChannelOnce()
    {
        RegistryHarness harness = new();
        ChannelControl ready = new("ready");
        ChannelControl linking = new("linking", blockStart: true);
        harness.Enqueue(ready);
        harness.Enqueue(linking);
        WorkerChannelRegistry registry = harness.Registry;
        await LinkAsync(registry, "ready");
        Task<WorkerChannel> link = LinkAsync(registry, "linking");
        await linking.StartEntered.WaitAsync(TestTimeout);

        Task firstDispose = registry.DisposeAsync().AsTask();
        Task repeatedDispose = registry.DisposeAsync().AsTask();

        Assert.Same(firstDispose, repeatedDispose);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => link.WaitAsync(TestTimeout));
        await firstDispose.WaitAsync(TestTimeout);
        Assert.Equal(1, ready.DisposeCount);
        Assert.Equal(1, linking.DisposeCount);
        ObjectDisposedException exception =
            await Assert.ThrowsAsync<ObjectDisposedException>(() => LinkAsync(registry, "late"));
        Assert.Equal(typeof(WorkerChannelRegistry).FullName, exception.ObjectName);
        Assert.False(await registry.UnlinkAsync("ready"));
        Assert.False(registry.TryGetInitializedChannel("ready", out _));
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
