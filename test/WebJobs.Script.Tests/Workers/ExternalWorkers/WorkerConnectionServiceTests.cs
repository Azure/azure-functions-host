// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class WorkerConnectionServiceTests
    {
        private readonly Mock<IConnectedWorkerChannelManager> _mockChannelManager;
        private readonly Mock<IScriptEventManager> _mockEventManager;
        private readonly Mock<IScriptHostManager> _mockHostManager;
        private readonly Mock<IOutboundGrpcClientFactory> _mockClientFactory;
        private readonly Mock<IOutboundGrpcClient> _mockGrpcClient;
        private readonly Mock<IConnectedWorkerChannel> _mockChannel;
        private readonly Mock<IConnectedWorkerChannelFactory> _mockChannelFactory;
        private readonly HostJsonContentProvider _hostJsonContentProvider;

        public WorkerConnectionServiceTests()
        {
            _mockChannelManager = new Mock<IConnectedWorkerChannelManager>();
            _mockEventManager = new Mock<IScriptEventManager>();
            _mockHostManager = new Mock<IScriptHostManager>();
            _mockGrpcClient = new Mock<IOutboundGrpcClient>();
            _mockClientFactory = new Mock<IOutboundGrpcClientFactory>();
            _mockClientFactory.Setup(f => f.Create()).Returns(_mockGrpcClient.Object);
            _mockChannel = new Mock<IConnectedWorkerChannel>();
            _mockChannelFactory = new Mock<IConnectedWorkerChannelFactory>();
            _hostJsonContentProvider = new HostJsonContentProvider();
        }

        [Fact]
        public void ActiveWorkerCount_InitiallyZero()
        {
            var service = CreateService();

            Assert.Equal(0, service.ActiveWorkerCount);
        }

        [Fact]
        public void GetWorkerStatuses_InitiallyEmpty()
        {
            var service = CreateService();

            Assert.Empty(service.GetWorkerStatuses());
        }

        [Fact]
        public void GetWorkerStatus_UnknownWorker_ReturnsNull()
        {
            var service = CreateService();

            Assert.Null(service.GetWorkerStatus("w_unknown"));
        }

        [Fact]
        public async Task StartAsync_NotEnabled_DoesNotConnect()
        {
            var options = Options.Create(new ExternalWorkerOptions { IsEnabled = false });
            var service = CreateService(options);

            await service.StartAsync(CancellationToken.None);

            Assert.Equal(0, service.ActiveWorkerCount);
            Assert.Empty(service.GetWorkerStatuses());
        }

        [Fact]
        public async Task StartAsync_EnabledWithNoEndpoint_DoesNotConnect()
        {
            var options = Options.Create(new ExternalWorkerOptions { IsEnabled = true, GrpcEndpoint = null });
            var service = CreateService(options);

            await service.StartAsync(CancellationToken.None);

            Assert.Equal(0, service.ActiveWorkerCount);
        }

        [Fact]
        public async Task ConnectWorkerAsync_HappyPath_SetsConnectedState()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            var info = service.GetWorkerStatus("w_1");
            Assert.Equal(WorkerConnectionState.Connected, info.State);
            Assert.Equal(1, service.ActiveWorkerCount);
        }

        [Fact]
        public async Task ConnectWorkerAsync_HappyPath_RegistersChannel()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            _mockChannelManager.Verify(m => m.AddChannel("w_1", _mockChannel.Object), Times.Once);
        }

        [Fact]
        public async Task ConnectWorkerAsync_HappyPath_ExtractsHostJson()
        {
            SetupFullConnectMocks();
            _mockChannel
                .Setup(c => c.GetCapabilityState("host_configuration_json"))
                .Returns("{\"version\":\"2.0\"}");

            var service = CreateService();

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            string hostJson = _hostJsonContentProvider.WaitForContent(TimeSpan.FromSeconds(1));
            Assert.Equal("{\"version\":\"2.0\"}", hostJson);
        }

        [Fact]
        public async Task ConnectWorkerAsync_HappyPath_StartsScriptHost()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            _mockHostManager.Verify(m => m.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ConnectWorkerAsync_FirstWorkerFails_SecondWorkerCanRetryAndSucceed()
        {
            SetupFullConnectMocks();

            // First call to StartAsync fails, second succeeds.
            int startAttempt = 0;
            _mockHostManager
                .Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (++startAttempt == 1)
                    {
                        return Task.FromException(new InvalidOperationException("transient failure"));
                    }

                    return Task.CompletedTask;
                });

            var service = CreateService();

            // First worker fails. Failed connects are removed from tracking so
            // the platform can immediately retry the same workerId without an
            // explicit DELETE; the failure surfaces only via the thrown exception.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_fail", new Uri("http://localhost:50051"), CancellationToken.None));

            Assert.Null(service.GetWorkerStatus("w_fail"));

            // Second worker succeeds — the recovery path reset _firstWorkerClaimed.
            await service.ConnectWorkerAsync("w_retry", new Uri("http://localhost:50052"), CancellationToken.None);

            var retryInfo = service.GetWorkerStatus("w_retry");
            Assert.Equal(WorkerConnectionState.Connected, retryInfo.State);
            Assert.Equal(2, _mockHostManager.Invocations.Count(i => i.Method.Name == nameof(IScriptHostManager.StartAsync)));
        }

        [Fact]
        public async Task ConnectWorkerAsync_DuplicateWorkerId_ThrowsAndPreservesExistingState()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_dup", new Uri("http://localhost:50051"), CancellationToken.None);

            var originalInfo = service.GetWorkerStatus("w_dup");
            Assert.Equal(WorkerConnectionState.Connected, originalInfo.State);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_dup", new Uri("http://localhost:50051"), CancellationToken.None));

            var afterInfo = service.GetWorkerStatus("w_dup");
            Assert.Equal(WorkerConnectionState.Connected, afterInfo.State);
            Assert.Same(originalInfo, afterInfo);
        }

        [Fact]
        public async Task ConnectWorkerAsync_Failure_RemovesWorkerAndPropagatesException()
        {
            SetupFullConnectMocks();
            _mockGrpcClient
                .Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("connection refused"));

            var service = CreateService();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_fail", new Uri("http://localhost:50051"), CancellationToken.None));

            Assert.Equal("connection refused", ex.Message);
            Assert.Null(service.GetWorkerStatus("w_fail"));
            Assert.DoesNotContain(service.GetWorkerStatuses(), w => w.WorkerId == "w_fail");
        }

        [Fact]
        public async Task DisconnectWorkerAsync_DelegatesToChannelManager()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_test", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.DisconnectWorkerAsync("w_test", CancellationToken.None);

            _mockChannelManager.Verify(m => m.ShutdownChannelAsync("w_test"), Times.Once);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_RemovesWorkerFromTracking()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_test", new Uri("http://localhost:50051"), CancellationToken.None);
            Assert.NotNull(service.GetWorkerStatus("w_test"));

            await service.DisconnectWorkerAsync("w_test", CancellationToken.None);

            Assert.Null(service.GetWorkerStatus("w_test"));
            Assert.Equal(0, service.ActiveWorkerCount);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_DisposesGrpcClient()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_test", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.DisconnectWorkerAsync("w_test", CancellationToken.None);

            _mockGrpcClient.Verify(c => c.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_UnknownWorker_NoOps()
        {
            var service = CreateService();

            await service.DisconnectWorkerAsync("w_unknown", CancellationToken.None);

            _mockChannelManager.Verify(m => m.ShutdownChannelAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_WhileConnecting_WaitsForConnectToComplete()
        {
            var connectPaused = new TaskCompletionSource();
            var connectCanProceed = new TaskCompletionSource();

            SetupFullConnectMocks();
            _mockGrpcClient
                .Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    connectPaused.TrySetResult();
                    await connectCanProceed.Task;
                });

            var service = CreateService();

            var connectTask = Task.Run(() =>
                service.ConnectWorkerAsync("w_race", new Uri("http://localhost:50051"), CancellationToken.None));

            await connectPaused.Task;

            var disconnectTask = service.DisconnectWorkerAsync("w_race", CancellationToken.None);

            // Let connect proceed to completion
            connectCanProceed.TrySetResult();

            await connectTask;
            await disconnectTask;

            // After disconnect, worker should be removed from tracking
            var finalInfo = service.GetWorkerStatus("w_race");
            Assert.Null(finalInfo);
        }

        [Fact]
        public async Task StopAsync_DisposesAllClients()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            var mockClient1 = new Mock<IOutboundGrpcClient>();
            var mockClient2 = new Mock<IOutboundGrpcClient>();
            int createCount = 0;
            _mockClientFactory
                .Setup(f => f.Create())
                .Returns(() => ++createCount == 1 ? mockClient1.Object : mockClient2.Object);

            // Need unique event channels per worker
            _mockEventManager
                .Setup(m => m.TryAddWorkerState(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(true);

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.ConnectWorkerAsync("w_2", new Uri("http://localhost:50052"), CancellationToken.None);

            await service.StopAsync(CancellationToken.None);

            mockClient1.Verify(c => c.DisposeAsync(), Times.Once);
            mockClient2.Verify(c => c.DisposeAsync(), Times.Once);
        }

        private void SetupFullConnectMocks()
        {
            _mockEventManager
                .Setup(m => m.TryAddWorkerState(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(true);

            _mockChannelFactory
                .Setup(f => f.Create(It.IsAny<string>(), It.IsAny<RpcWorkerConfig>()))
                .Returns(_mockChannel.Object);
        }

        private WorkerConnectionService CreateService(IOptions<ExternalWorkerOptions> options = null)
            => CreateService(options, null);

        private WorkerConnectionService CreateService(IOptions<ExternalWorkerOptions> options, IRuntimeStateManager runtimeStateManager)
        {
            options ??= Options.Create(new ExternalWorkerOptions { IsEnabled = true });

            return new WorkerConnectionService(
                _mockChannelFactory.Object,
                _mockChannelManager.Object,
                _mockEventManager.Object,
                _mockHostManager.Object,
                _mockClientFactory.Object,
                options,
                _hostJsonContentProvider,
                runtimeStateManager ?? Mock.Of<IRuntimeStateManager>(),
                NullLoggerFactory.Instance);
        }

        [Fact]
        public async Task ConnectWorkerAsync_SubscribesToDrainRequestedEvent()
        {
            SetupFullConnectMocks();
            var service = CreateService();

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            _mockChannel.VerifyAdd(c => c.DrainRequested += It.IsAny<Action<string>>(), Times.Once);
        }

        [Fact]
        public async Task DrainWorker_MarksChannelAsDraining()
        {
            SetupFullConnectMocks();
            _mockChannelManager
                .Setup(m => m.GetChannel("w_1"))
                .Returns(_mockChannel.Object);

            var service = CreateService();
            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            // DisconnectWorkerAsync is what OnWorkerDrainRequested calls (fire-and-forget).
            // Calling it directly avoids flaky Task.Delay waits.
            await service.DisconnectWorkerAsync("w_1", CancellationToken.None);

            _mockChannel.Verify(c => c.BeginDrain(), Times.Once);
        }

        [Fact]
        public async Task DrainWorker_SendsDrainComplete()
        {
            SetupFullConnectMocks();
            _mockChannelManager
                .Setup(m => m.GetChannel("w_1"))
                .Returns(_mockChannel.Object);

            var service = CreateService();
            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            await service.DisconnectWorkerAsync("w_1", CancellationToken.None);

            _mockChannel.Verify(c => c.SendWorkerDrainComplete(), Times.Once);
        }

        [Fact]
        public async Task DrainAndDisconnectAllAsync_DisconnectsAllWorkers()
        {
            SetupFullConnectMocks();

            var mockClient1 = new Mock<IOutboundGrpcClient>();
            var mockClient2 = new Mock<IOutboundGrpcClient>();
            int createCount = 0;
            _mockClientFactory
                .Setup(f => f.Create())
                .Returns(() => ++createCount == 1 ? mockClient1.Object : mockClient2.Object);

            _mockEventManager
                .Setup(m => m.TryAddWorkerState(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(true);

            var service = CreateService();
            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.ConnectWorkerAsync("w_2", new Uri("http://localhost:50052"), CancellationToken.None);

            Assert.Equal(2, service.ActiveWorkerCount);

            await service.DrainAndDisconnectAllAsync(CancellationToken.None);

            Assert.Equal(0, service.ActiveWorkerCount);
            Assert.Empty(service.GetWorkerStatuses());
        }

        [Fact]
        public async Task ConnectWorkerAsync_WhileStopping_Throws()
        {
            SetupFullConnectMocks();

            var mockClient1 = new Mock<IOutboundGrpcClient>();
            _mockClientFactory
                .Setup(f => f.Create())
                .Returns(mockClient1.Object);

            var service = CreateService();
            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            // Initiate stop — this sets the _stopping flag
            await service.DrainAndDisconnectAllAsync(CancellationToken.None);

            // Attempting to connect a new worker after stop should fail
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_late", new Uri("http://localhost:50053"), CancellationToken.None));

            Assert.Contains("stopping", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ConnectWorkerAsync_CallsOnWorkerLinkedAndOnWorkerCapacityAvailable()
        {
            SetupFullConnectMocks();
            var mockRuntimeState = new Mock<IRuntimeStateManager>(MockBehavior.Strict);
            mockRuntimeState.Setup(m => m.OnWorkerLinked("w_1"));
            mockRuntimeState.Setup(m => m.OnWorkerCapacityAvailable("w_1", It.IsAny<int>()));

            var service = CreateService(null, mockRuntimeState.Object);

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            mockRuntimeState.Verify(m => m.OnWorkerLinked("w_1"), Times.Once);
            mockRuntimeState.Verify(m => m.OnWorkerCapacityAvailable("w_1", It.Is<int>(n => n > 0)), Times.Once);
        }

        [Fact]
        public async Task ConnectWorkerAsync_CallsOnWorkerLinkedBeforeOnWorkerCapacityAvailable()
        {
            SetupFullConnectMocks();
            var sequence = new System.Collections.Generic.List<string>();
            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            mockRuntimeState
                .Setup(m => m.OnWorkerLinked(It.IsAny<string>()))
                .Callback<string>(id => sequence.Add($"linked:{id}"));
            mockRuntimeState
                .Setup(m => m.OnWorkerCapacityAvailable(It.IsAny<string>(), It.IsAny<int>()))
                .Callback<string, int>((id, _) => sequence.Add($"capacity:{id}"));

            var service = CreateService(null, mockRuntimeState.Object);

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);

            Assert.Equal(new[] { "linked:w_1", "capacity:w_1" }, sequence);
        }

        [Fact]
        public async Task ConnectWorkerAsync_Failure_LinkedButNoCapacityPublished()
        {
            SetupFullConnectMocks();
            _mockGrpcClient
                .Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("connection refused"));

            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            var service = CreateService(null, mockRuntimeState.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_fail", new Uri("http://localhost:50051"), CancellationToken.None));

            mockRuntimeState.Verify(m => m.OnWorkerLinked("w_fail"), Times.Once);
            mockRuntimeState.Verify(m => m.OnWorkerCapacityAvailable(It.IsAny<string>(), It.IsAny<int>()), Times.Never);

            // OnWorkerLinked must be reversed by OnWorkerUnlinked, otherwise a failed
            // connect permanently inflates LinkedWorkerCount and can eventually block
            // the platform from linking new workers.
            mockRuntimeState.Verify(m => m.OnWorkerUnlinked("w_fail"), Times.Once);

            // The worker must also be removed from the service's tracking so the
            // platform can retry the same workerId after clearing the Error state.
            Assert.Null(service.GetWorkerStatus("w_fail"));
            Assert.DoesNotContain(service.GetWorkerStatuses(), w => w.WorkerId == "w_fail");
        }

        [Fact]
        public async Task DisconnectWorkerAsync_Failure_RemovesWorkerAndUnlinks()
        {
            SetupFullConnectMocks();
            _mockChannelManager
                .Setup(m => m.GetChannel("w_fail"))
                .Returns(_mockChannel.Object);

            // Make the drain throw a non-timeout exception so we hit the catch block.
            _mockChannel
                .Setup(c => c.DrainInvocationsAsync())
                .ThrowsAsync(new InvalidOperationException("drain failed"));

            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            var service = CreateService(null, mockRuntimeState.Object);

            await service.ConnectWorkerAsync("w_fail", new Uri("http://localhost:50051"), CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DisconnectWorkerAsync("w_fail", CancellationToken.None));

            // Even on a failed disconnect the worker must not linger as "linked",
            // otherwise LinkedWorkerCount drifts upward across retries.
            mockRuntimeState.Verify(m => m.OnWorkerUnlinked("w_fail"), Times.Once);
            Assert.Null(service.GetWorkerStatus("w_fail"));
            Assert.DoesNotContain(service.GetWorkerStatuses(), w => w.WorkerId == "w_fail");
        }

        [Fact]
        public async Task DisconnectWorkerAsync_CallsCapacityUnavailableThenUnlinked()
        {
            SetupFullConnectMocks();
            _mockChannelManager
                .Setup(m => m.GetChannel("w_1"))
                .Returns(_mockChannel.Object);

            var sequence = new System.Collections.Generic.List<string>();
            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            mockRuntimeState
                .Setup(m => m.OnWorkerLinked(It.IsAny<string>()))
                .Callback<string>(id => sequence.Add($"linked:{id}"));
            mockRuntimeState
                .Setup(m => m.OnWorkerCapacityAvailable(It.IsAny<string>(), It.IsAny<int>()))
                .Callback<string, int>((id, _) => sequence.Add($"capacity:{id}"));
            mockRuntimeState
                .Setup(m => m.OnWorkerCapacityUnavailable(It.IsAny<string>()))
                .Callback<string>(id => sequence.Add($"capacity-unavailable:{id}"));
            mockRuntimeState
                .Setup(m => m.OnWorkerUnlinked(It.IsAny<string>()))
                .Callback<string>(id => sequence.Add($"unlinked:{id}"));

            var service = CreateService(null, mockRuntimeState.Object);

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.DisconnectWorkerAsync("w_1", CancellationToken.None);

            Assert.Equal(
                new[] { "linked:w_1", "capacity:w_1", "capacity-unavailable:w_1", "unlinked:w_1" },
                sequence);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_CapacityUnavailableHappensBeforeChannelBeginDrain()
        {
            SetupFullConnectMocks();
            _mockChannelManager
                .Setup(m => m.GetChannel("w_1"))
                .Returns(_mockChannel.Object);

            var sequence = new System.Collections.Generic.List<string>();
            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            mockRuntimeState
                .Setup(m => m.OnWorkerCapacityUnavailable(It.IsAny<string>()))
                .Callback<string>(id => sequence.Add($"capacity-unavailable:{id}"));
            _mockChannel
                .Setup(c => c.BeginDrain())
                .Callback(() => sequence.Add("begin-drain"));

            var service = CreateService(null, mockRuntimeState.Object);

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);
            sequence.Clear();
            await service.DisconnectWorkerAsync("w_1", CancellationToken.None);

            int capacityIdx = sequence.IndexOf("capacity-unavailable:w_1");
            int drainIdx = sequence.IndexOf("begin-drain");
            Assert.True(capacityIdx >= 0);
            Assert.True(drainIdx >= 0);
            Assert.True(capacityIdx < drainIdx, $"Expected capacity withdraw before BeginDrain, got sequence: {string.Join(",", sequence)}");
        }

        [Fact]
        public async Task DrainAndDisconnectAllAsync_CallsSetStoppingOnce()
        {
            SetupFullConnectMocks();

            var mockClient1 = new Mock<IOutboundGrpcClient>();
            var mockClient2 = new Mock<IOutboundGrpcClient>();
            int createCount = 0;
            _mockClientFactory
                .Setup(f => f.Create())
                .Returns(() => ++createCount == 1 ? mockClient1.Object : mockClient2.Object);

            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            var service = CreateService(null, mockRuntimeState.Object);

            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.ConnectWorkerAsync("w_2", new Uri("http://localhost:50052"), CancellationToken.None);

            await service.DrainAndDisconnectAllAsync(CancellationToken.None);

            mockRuntimeState.Verify(m => m.SetStopping(), Times.Once);
        }

        [Fact]
        public async Task DrainAndDisconnectAllAsync_CallsSetStoppingBeforeAnyWorkerUnlinked()
        {
            SetupFullConnectMocks();

            var mockClient1 = new Mock<IOutboundGrpcClient>();
            var mockClient2 = new Mock<IOutboundGrpcClient>();
            int createCount = 0;
            _mockClientFactory
                .Setup(f => f.Create())
                .Returns(() => ++createCount == 1 ? mockClient1.Object : mockClient2.Object);

            var sequence = new System.Collections.Generic.List<string>();
            var mockRuntimeState = new Mock<IRuntimeStateManager>();
            mockRuntimeState
                .Setup(m => m.SetStopping())
                .Callback(() => sequence.Add("stopping"));
            mockRuntimeState
                .Setup(m => m.OnWorkerUnlinked(It.IsAny<string>()))
                .Callback<string>(id => sequence.Add($"unlinked:{id}"));

            var service = CreateService(null, mockRuntimeState.Object);
            await service.ConnectWorkerAsync("w_1", new Uri("http://localhost:50051"), CancellationToken.None);
            await service.ConnectWorkerAsync("w_2", new Uri("http://localhost:50052"), CancellationToken.None);

            await service.DrainAndDisconnectAllAsync(CancellationToken.None);

            int stoppingIdx = sequence.IndexOf("stopping");
            Assert.True(stoppingIdx >= 0);
            Assert.All(
                sequence.Where(s => s.StartsWith("unlinked:", StringComparison.Ordinal)),
                unlinked => Assert.True(sequence.IndexOf(unlinked) > stoppingIdx));
        }
    }
}
