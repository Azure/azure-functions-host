// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class ConnectedWorkerChannelManagerTests
    {
        private readonly ConnectedWorkerChannelManager _manager = new();

        private static ConnectedWorkerChannel CreateTestChannel(string workerId)
        {
            var eventManager = new ScriptEventManager();
            eventManager.AddGrpcChannels(workerId);

            var workerConfig = TestHelpers.GetTestWorkerConfigs().First();
            var mockHostOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            mockHostOptions.Setup(o => o.CurrentValue).Returns(new ScriptApplicationHostOptions { ScriptPath = @"c:\testdir" });

            return new ConnectedWorkerChannel(
                workerId,
                eventManager,
                new Mock<IScriptHostManager>().Object,
                workerConfig,
                NullLogger.Instance,
                new Mock<IMetricsLogger>().Object,
                new TestEnvironment(),
                mockHostOptions.Object,
                new Mock<ISharedMemoryManager>().Object,
                Options.Create(new WorkerConcurrencyOptions()),
                Options.Create(new FunctionsHostingConfigOptions()),
                new Mock<IHttpProxyService>().Object);
        }

        [Fact]
        public async Task AddChannel_SignalsWaitForChannelAsync()
        {
            var channel = CreateTestChannel("worker1");
            _manager.AddChannel("worker1", channel);

            var result = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            Assert.Same(channel, result);
        }

        [Fact]
        public async Task WaitForChannelAsync_BlocksUntilChannelAdded()
        {
            var waitTask = _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            await Task.Delay(50);
            Assert.False(waitTask.IsCompleted);

            var channel = CreateTestChannel("worker1");
            _manager.AddChannel("worker1", channel);

            var result = await waitTask;

            Assert.Same(channel, result);
        }

        [Fact]
        public async Task WaitForChannelAsync_TimesOut()
        {
            await Assert.ThrowsAsync<TimeoutException>(
                () => _manager.WaitForChannelAsync(TimeSpan.FromMilliseconds(100)));
        }

        [Fact]
        public async Task WaitForChannelAsync_ReturnsCachedChannel()
        {
            var channel = CreateTestChannel("worker1");
            _manager.AddChannel("worker1", channel);

            var result1 = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            var result2 = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            Assert.Same(channel, result1);
            Assert.Same(channel, result2);
        }

        [Fact]
        public void GetChannel_ReturnsNull_WhenNotFound()
        {
            var result = _manager.GetChannel("unknown");

            Assert.Null(result);
        }

        [Fact]
        public void GetChannel_ReturnsChannel_WhenFound()
        {
            var channel = CreateTestChannel("worker1");
            _manager.AddChannel("worker1", channel);

            var result = _manager.GetChannel("worker1");

            Assert.Same(channel, result);
        }

        [Fact]
        public void GetChannels_ReturnsAllChannels()
        {
            var channel1 = CreateTestChannel("worker1");
            var channel2 = CreateTestChannel("worker2");
            _manager.AddChannel("worker1", channel1);
            _manager.AddChannel("worker2", channel2);

            var result = _manager.GetChannels();

            Assert.Equal(2, result.Count);
            Assert.Same(channel1, result["worker1"]);
            Assert.Same(channel2, result["worker2"]);
        }

        [Fact]
        public async Task ShutdownChannelAsync_RemovesChannel()
        {
            var channel = CreateTestChannel("worker1");
            _manager.AddChannel("worker1", channel);
            Assert.NotNull(_manager.GetChannel("worker1"));

            await _manager.ShutdownChannelAsync("worker1");

            Assert.Null(_manager.GetChannel("worker1"));
        }

        [Fact]
        public async Task WaitForChannelAsync_AfterShutdownAndReconnect_ReturnsNewChannel()
        {
            var channelA = CreateTestChannel("workerA");
            _manager.AddChannel("workerA", channelA);

            var result1 = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            Assert.Same(channelA, result1);

            await _manager.ShutdownChannelAsync("workerA");

            var waitTask = _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            Assert.False(waitTask.IsCompleted, "WaitForChannelAsync should block after all channels are shut down");

            var channelB = CreateTestChannel("workerB");
            _manager.AddChannel("workerB", channelB);

            var result2 = await waitTask;
            Assert.Same(channelB, result2);
        }

        [Fact]
        public async Task WaitForChannelAsync_AfterPartialShutdown_StillReturnsRemainingChannel()
        {
            var channelA = CreateTestChannel("workerA");
            var channelB = CreateTestChannel("workerB");
            _manager.AddChannel("workerA", channelA);
            _manager.AddChannel("workerB", channelB);

            await _manager.ShutdownChannelAsync("workerA");

            // TCS should not have been reset since channelB is still active
            var result = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(result);
            Assert.Same(channelB, result);
        }
    }
}
