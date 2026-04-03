// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
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
        private readonly HostJsonContentProvider _hostJsonContentProvider;

        public WorkerConnectionServiceTests()
        {
            _mockChannelManager = new Mock<IConnectedWorkerChannelManager>();
            _mockEventManager = new Mock<IScriptEventManager>();
            _mockHostManager = new Mock<IScriptHostManager>();
            _hostJsonContentProvider = new HostJsonContentProvider();
        }

        [Fact]
        public void ActiveWorkerCount_InitiallyZero()
        {
            var service = CreateMinimalService();

            Assert.Equal(0, service.ActiveWorkerCount);
        }

        [Fact]
        public void GetWorkerStatuses_InitiallyEmpty()
        {
            var service = CreateMinimalService();

            var statuses = service.GetWorkerStatuses();

            Assert.Empty(statuses);
        }

        [Fact]
        public void GetWorkerStatus_UnknownWorker_ReturnsNull()
        {
            var service = CreateMinimalService();

            var status = service.GetWorkerStatus("w_unknown");

            Assert.Null(status);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_DelegatesToChannelManager()
        {
            var service = CreateMinimalService();

            // Seed a worker entry. ConnectWorkerAsync writes to _workers immediately,
            // then fails at AddGrpcChannels (mock returns false), leaving an Error entry.
            await Assert.ThrowsAnyAsync<Exception>(
                () => service.ConnectWorkerAsync("w_test", new Uri("http://localhost:50051"), CancellationToken.None));

            await service.DisconnectWorkerAsync("w_test", CancellationToken.None);

            _mockChannelManager.Verify(m => m.ShutdownChannelAsync("w_test"), Times.Once);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_UpdatesStateToDraining()
        {
            var service = CreateMinimalService();

            // Seed a worker entry via a failed connect.
            await Assert.ThrowsAnyAsync<Exception>(
                () => service.ConnectWorkerAsync("w_test", new Uri("http://localhost:50051"), CancellationToken.None));

            // Set up a delayed shutdown to observe the Draining state
            var tcs = new TaskCompletionSource();
            _mockChannelManager
                .Setup(m => m.ShutdownChannelAsync("w_test"))
                .Returns(() =>
                {
                    // While draining, the state should be Draining
                    var info = service.GetWorkerStatus("w_test");
                    Assert.Equal(WorkerConnectionState.Draining, info.State);
                    tcs.SetResult();
                    return Task.CompletedTask;
                });

            await service.DisconnectWorkerAsync("w_test", CancellationToken.None);

            Assert.True(tcs.Task.IsCompleted);

            var finalInfo = service.GetWorkerStatus("w_test");
            Assert.Equal(WorkerConnectionState.Disconnected, finalInfo.State);
        }

        [Fact]
        public async Task StartAsync_NotEnabled_DoesNotConnect()
        {
            var options = Options.Create(new ExternalWorkerOptions { IsEnabled = false });
            var service = CreateMinimalService(options);

            await service.StartAsync(CancellationToken.None);

            Assert.Equal(0, service.ActiveWorkerCount);
            Assert.Empty(service.GetWorkerStatuses());
        }

        [Fact]
        public async Task StartAsync_EnabledWithNoEndpoint_DoesNotConnect()
        {
            var options = Options.Create(new ExternalWorkerOptions { IsEnabled = true, GrpcEndpoint = null });
            var service = CreateMinimalService(options);

            await service.StartAsync(CancellationToken.None);

            Assert.Equal(0, service.ActiveWorkerCount);
        }

        [Fact]
        public async Task ConnectWorkerAsync_DuplicateWorkerId_ThrowsAndPreservesExistingState()
        {
            var service = CreateMinimalService();

            // First connect: will fail at AddGrpcChannels (mock TryAddWorkerState returns false),
            // leaving the worker in Error state.
            await Assert.ThrowsAnyAsync<Exception>(
                () => service.ConnectWorkerAsync("w_dup", new Uri("http://localhost:50051"), CancellationToken.None));

            var originalInfo = service.GetWorkerStatus("w_dup");
            Assert.NotNull(originalInfo);
            Assert.Equal(WorkerConnectionState.Error, originalInfo.State);

            // Second connect with the same workerId should throw InvalidOperationException
            // and the original state should remain unchanged.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectWorkerAsync("w_dup", new Uri("http://localhost:50051"), CancellationToken.None));

            var afterInfo = service.GetWorkerStatus("w_dup");
            Assert.Equal(WorkerConnectionState.Error, afterInfo.State);
            Assert.Same(originalInfo, afterInfo);
        }

        [Fact]
        public async Task DisconnectWorkerAsync_WhileConnecting_WaitsForConnectToComplete()
        {
            var service = CreateMinimalService();

            // Make AddGrpcChannels succeed but block inside it via a TCS so connect
            // is mid-flight when we call disconnect.
            var connectPaused = new TaskCompletionSource();
            var connectCanProceed = new TaskCompletionSource();

            // First call to TryAddWorkerState (inbound channel): succeed and signal pause
            // Second call (outbound channel): wait for proceed signal, then succeed
            int addCallCount = 0;
            _mockEventManager
                .Setup(m => m.TryAddWorkerState(It.IsAny<string>(), It.IsAny<object>()))
                .Returns<string, object>((_, _) =>
                {
                    int call = Interlocked.Increment(ref addCallCount);
                    if (call == 1)
                    {
                        // First add (inbound): succeed, signal that connect is paused
                        connectPaused.TrySetResult();
                        return true;
                    }
                    else if (call == 2)
                    {
                        // Second add (outbound): block until disconnect starts
                        connectCanProceed.Task.GetAwaiter().GetResult();
                        return true;
                    }

                    return true;
                });

            // Start connect in background — it will pause during AddGrpcChannels
            var connectTask = Task.Run(() =>
                service.ConnectWorkerAsync("w_race", new Uri("http://localhost:50051"), CancellationToken.None));

            // Wait for connect to be mid-flight
            await connectPaused.Task;

            // Now call disconnect while connect is blocked
            var disconnectTask = service.DisconnectWorkerAsync("w_race", CancellationToken.None);

            // BUG: Without the TCS fix, disconnect completes immediately (state = Disconnected),
            // then connect resumes and overwrites the state.
            // With the fix, disconnect should wait for connect to finish first.

            // Let connect proceed — it will fail at ConnectAsync since mocks aren't fully set up
            connectCanProceed.TrySetResult();

            // Both tasks should complete (connect will throw since gRPC won't actually connect)
            await Assert.ThrowsAnyAsync<Exception>(() => connectTask);
            await disconnectTask;

            // Final state should be Disconnected (from disconnect), not Error (from connect)
            var finalInfo = service.GetWorkerStatus("w_race");
            Assert.NotNull(finalInfo);
            Assert.Equal(WorkerConnectionState.Disconnected, finalInfo.State);
        }

        private WorkerConnectionService CreateMinimalService(IOptions<ExternalWorkerOptions> options = null)
        {
            options ??= Options.Create(new ExternalWorkerOptions { IsEnabled = true });

            var mockFactory = new Mock<ConnectedWorkerChannelFactory>(
                _mockEventManager.Object,
                _mockHostManager.Object,
                Mock.Of<IEnvironment>(),
                Mock.Of<IOptionsMonitor<ScriptApplicationHostOptions>>(),
                Mock.Of<ISharedMemoryManager>(),
                Mock.Of<IOptions<WorkerConcurrencyOptions>>(),
                Mock.Of<IOptions<FunctionsHostingConfigOptions>>(),
                Mock.Of<IHttpProxyService>(),
                NullLoggerFactory.Instance,
                Mock.Of<IMetricsLogger>());

            return new WorkerConnectionService(
                mockFactory.Object,
                _mockChannelManager.Object,
                _mockEventManager.Object,
                _mockHostManager.Object,
                options,
                _hostJsonContentProvider,
                NullLoggerFactory.Instance);
        }
    }
}
