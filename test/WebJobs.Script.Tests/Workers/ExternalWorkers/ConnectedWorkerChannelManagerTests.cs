// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class ConnectedWorkerChannelManagerTests
    {
        private readonly ConnectedWorkerChannelManager _manager = new();

        private static Mock<IRpcWorkerChannel> CreateMockChannel(string workerId, bool ready = false)
        {
            var mock = new Mock<IRpcWorkerChannel>();
            mock.Setup(c => c.Id).Returns(workerId);
            mock.Setup(c => c.IsChannelReadyForInvocations()).Returns(ready);
            return mock;
        }

        [Fact]
        public async Task AddChannel_SignalsWaitForChannelAsync()
        {
            var channel = CreateMockChannel("worker1", ready: true);
            _manager.AddChannel("worker1", channel.Object);

            var result = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            Assert.Same(channel.Object, result);
        }

        [Fact]
        public async Task WaitForChannelAsync_BlocksUntilChannelAdded()
        {
            var waitTask = _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            await Task.Delay(50);
            Assert.False(waitTask.IsCompleted);

            var channel = CreateMockChannel("worker1", ready: true);
            _manager.AddChannel("worker1", channel.Object);

            var result = await waitTask;

            Assert.Same(channel.Object, result);
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
            var channel = CreateMockChannel("worker1", ready: true);
            _manager.AddChannel("worker1", channel.Object);

            var result1 = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            var result2 = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            Assert.Same(channel.Object, result1);
            Assert.Same(channel.Object, result2);
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
            var channel = CreateMockChannel("worker1");
            _manager.AddChannel("worker1", channel.Object);

            var result = _manager.GetChannel("worker1");

            Assert.Same(channel.Object, result);
        }

        [Fact]
        public void GetChannels_ReturnsAllChannels()
        {
            var channel1 = CreateMockChannel("worker1");
            var channel2 = CreateMockChannel("worker2");
            _manager.AddChannel("worker1", channel1.Object);
            _manager.AddChannel("worker2", channel2.Object);

            var result = _manager.GetChannels();

            Assert.Equal(2, result.Count);
            Assert.Same(channel1.Object, result["worker1"]);
            Assert.Same(channel2.Object, result["worker2"]);
        }

        [Fact]
        public async Task ShutdownChannelAsync_RemovesChannel()
        {
            var channel = CreateMockChannel("worker1");
            _manager.AddChannel("worker1", channel.Object);
            Assert.NotNull(_manager.GetChannel("worker1"));

            await _manager.ShutdownChannelAsync("worker1");

            Assert.Null(_manager.GetChannel("worker1"));
        }

        [Fact]
        public async Task WaitForChannelAsync_AfterShutdownAndReconnect_ReturnsNewChannel()
        {
            var channelA = CreateMockChannel("workerA", ready: true);
            _manager.AddChannel("workerA", channelA.Object);

            var result1 = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            Assert.Same(channelA.Object, result1);

            await _manager.ShutdownChannelAsync("workerA");

            var waitTask = _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            Assert.False(waitTask.IsCompleted, "WaitForChannelAsync should block after all channels are shut down");

            var channelB = CreateMockChannel("workerB", ready: true);
            _manager.AddChannel("workerB", channelB.Object);

            var result2 = await waitTask;
            Assert.Same(channelB.Object, result2);
        }

        [Fact]
        public async Task WaitForChannelAsync_AfterPartialShutdown_StillReturnsRemainingChannel()
        {
            var channelA = CreateMockChannel("workerA", ready: true);
            var channelB = CreateMockChannel("workerB", ready: true);
            _manager.AddChannel("workerA", channelA.Object);
            _manager.AddChannel("workerB", channelB.Object);

            await _manager.ShutdownChannelAsync("workerA");

            var result = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(result);
            Assert.Same(channelB.Object, result);
        }

        [Fact]
        public async Task WaitForChannelAsync_ChannelAddedButNotReady_ReturnsChannel()
        {
            // WaitForChannelAsync returns any connected channel — readiness
            // for invocations is the dispatcher's concern, not the manager's.
            var channel = CreateMockChannel("worker1", ready: false);
            _manager.AddChannel("worker1", channel.Object);

            var result = await _manager.WaitForChannelAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(result);
            Assert.Same(channel.Object, result);
        }
    }
}
