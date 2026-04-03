// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        public async Task ConnectWorkerAsync_Failure_SetsErrorState()
        {
            SetupFullConnectMocks();
            _mockGrpcClient
                .Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("connection refused"));

            var service = CreateService();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_fail", new Uri("http://localhost:50051"), CancellationToken.None));

            var info = service.GetWorkerStatus("w_fail");
            Assert.Equal(WorkerConnectionState.Error, info.State);
            Assert.Equal("connection refused", info.ErrorMessage);
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

            // Observe Draining state mid-disconnect
            _mockChannelManager
                .Setup(m => m.ShutdownChannelAsync("w_test"))
                .Returns(() =>
                {
                    var info = service.GetWorkerStatus("w_test");
                    Assert.Equal(WorkerConnectionState.Draining, info.State);
                    return Task.CompletedTask;
                });

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
                NullLoggerFactory.Instance);
        }
    }
}
