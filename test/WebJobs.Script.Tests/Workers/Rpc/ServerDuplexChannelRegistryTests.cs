// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class ServerDuplexChannelRegistryTests
    {
        [Fact]
        public async Task CreateRegisteredChannel_ExposesBothMessageDirections()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> channel = channelRegistry.CreateRegisteredChannel(workerId);
            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out FunctionRpcServiceEndpoints serviceEndpoints));

            var outbound = new StreamingMessage { RequestId = "outbound" };
            Assert.True(channel.Writer.TryWrite(outbound));
            Assert.True(serviceEndpoints.HostToWorkerReader.TryRead(out StreamingMessage receivedOutbound));

            var inbound = new StreamingMessage { RequestId = "inbound" };
            Assert.True(serviceEndpoints.WorkerToHostWriter.TryWrite(inbound));
            Assert.True(channel.Reader.TryRead(out StreamingMessage receivedInbound));

            Assert.Same(outbound, receivedOutbound);
            Assert.Same(inbound, receivedInbound);

            await channel.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_UnregistersAndCompletesBothDirections()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> channel = channelRegistry.CreateRegisteredChannel(workerId);
            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out FunctionRpcServiceEndpoints serviceEndpoints));

            await channel.DisposeAsync();

            Assert.False(channelRegistry.TryGetServiceEndpoints(workerId, out _));
            Assert.True(channel.Reader.Completion.IsCompletedSuccessfully);
            Assert.True(serviceEndpoints.HostToWorkerReader.Completion.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task CreateRegisteredChannel_WithDuplicateWorkerId_PreservesExistingChannel()
        {
            const string workerId = "worker-id";
            var channelRegistry = new TestServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> channel = channelRegistry.CreateRegisteredChannel(workerId);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => channelRegistry.CreateRegisteredChannel(workerId));

            Assert.Equal(nameof(workerId), exception.ParamName);
            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out _));
            Assert.False(channel.Reader.Completion.IsCompleted);
            Assert.True(channelRegistry.Channels[1].Reader.Completion.IsCompletedSuccessfully);

            await channel.DisposeAsync();
        }

        [Fact]
        public async Task CreateRegisteredChannel_AfterDisposal_ReusesWorkerId()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> originalChannel = channelRegistry.CreateRegisteredChannel(workerId);
            await originalChannel.DisposeAsync();

            DuplexChannel<StreamingMessage> replacementChannel = channelRegistry.CreateRegisteredChannel(workerId);

            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out _));

            await replacementChannel.DisposeAsync();
        }

        private sealed class TestServerDuplexChannelRegistry : ServerDuplexChannelRegistry
        {
            public List<ServerDuplexChannel> Channels { get; } = new();

            protected override ServerDuplexChannel CreateChannel()
            {
                var channel = new ServerDuplexChannel();
                Channels.Add(channel);
                return channel;
            }
        }
    }
}
