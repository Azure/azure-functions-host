// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class ServerDuplexChannelTests
    {
        [Fact]
        public async Task AddServerDuplexChannel_StoresChannelAndExposesBothMessageDirections()
        {
            const string workerId = "worker-id";
            using var eventManager = new ScriptEventManager();
            ServerDuplexChannel channel = eventManager.AddServerDuplexChannel(workerId);
            Assert.True(eventManager.TryGetServerDuplexChannel(workerId, out ServerDuplexChannel registeredChannel));

            ServerDuplexChannelEndpoints serverEndpoints = registeredChannel.ServerEndpoints;
            var outbound = new StreamingMessage { RequestId = "outbound" };
            Assert.True(channel.Writer.TryWrite(outbound));
            Assert.True(serverEndpoints.HostToWorkerReader.TryRead(out StreamingMessage receivedOutbound));

            var inbound = new StreamingMessage { RequestId = "inbound" };
            Assert.True(serverEndpoints.WorkerToHostWriter.TryWrite(inbound));
            Assert.True(channel.Reader.TryRead(out StreamingMessage receivedInbound));

            Assert.Same(channel, registeredChannel);
            Assert.Same(outbound, receivedOutbound);
            Assert.Same(inbound, receivedInbound);

            await channel.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_CompletesBothDirections()
        {
            const string workerId = "worker-id";
            using var eventManager = new ScriptEventManager();
            ServerDuplexChannel channel = eventManager.AddServerDuplexChannel(workerId);
            ServerDuplexChannelEndpoints serverEndpoints = channel.ServerEndpoints;

            await channel.DisposeAsync();

            Assert.True(channel.Reader.Completion.IsCompletedSuccessfully);
            Assert.True(serverEndpoints.HostToWorkerReader.Completion.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task AddServerDuplexChannel_WithDuplicateWorkerId_PreservesExistingChannel()
        {
            const string workerId = "worker-id";
            using var eventManager = new ScriptEventManager();
            ServerDuplexChannel channel = eventManager.AddServerDuplexChannel(workerId);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => eventManager.AddServerDuplexChannel(workerId));

            Assert.Equal(nameof(workerId), exception.ParamName);
            Assert.True(eventManager.TryGetServerDuplexChannel(workerId, out ServerDuplexChannel registeredChannel));
            Assert.Same(channel, registeredChannel);
            Assert.False(channel.Reader.Completion.IsCompleted);

            await channel.DisposeAsync();
        }
    }
}
