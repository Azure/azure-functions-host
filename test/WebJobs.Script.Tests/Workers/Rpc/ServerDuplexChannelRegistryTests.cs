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
        public async Task CreateLease_ExposesBothMessageDirections()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> lease = channelRegistry.CreateLease(workerId);
            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out FunctionRpcChannelEndpoints serviceEndpoints));

            var outbound = new StreamingMessage { RequestId = "outbound" };
            Assert.True(lease.Writer.TryWrite(outbound));
            Assert.True(serviceEndpoints.HostToWorkerReader.TryRead(out StreamingMessage receivedOutbound));

            var inbound = new StreamingMessage { RequestId = "inbound" };
            Assert.True(serviceEndpoints.WorkerToHostWriter.TryWrite(inbound));
            Assert.True(lease.Reader.TryRead(out StreamingMessage receivedInbound));

            Assert.Same(outbound, receivedOutbound);
            Assert.Same(inbound, receivedInbound);

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_ReleasesLeaseAndCompletesBothDirections()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> lease = channelRegistry.CreateLease(workerId);
            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out FunctionRpcChannelEndpoints serviceEndpoints));

            await lease.DisposeAsync();

            Assert.False(channelRegistry.TryGetServiceEndpoints(workerId, out _));
            Assert.True(lease.Reader.Completion.IsCompletedSuccessfully);
            Assert.True(serviceEndpoints.HostToWorkerReader.Completion.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task CreateLease_WithDuplicateWorkerId_PreservesExistingLease()
        {
            const string workerId = "worker-id";
            var channelRegistry = new TestServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> lease = channelRegistry.CreateLease(workerId);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => channelRegistry.CreateLease(workerId));

            Assert.Equal(nameof(workerId), exception.ParamName);
            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out _));
            Assert.False(lease.Reader.Completion.IsCompleted);
            Assert.True(channelRegistry.Channels[1].Reader.Completion.IsCompletedSuccessfully);

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task CreateLease_AfterDisposal_ReusesWorkerId()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            DuplexChannel<StreamingMessage> originalLease = channelRegistry.CreateLease(workerId);
            await originalLease.DisposeAsync();

            DuplexChannel<StreamingMessage> replacementLease = channelRegistry.CreateLease(workerId);

            Assert.True(channelRegistry.TryGetServiceEndpoints(workerId, out _));

            await replacementLease.DisposeAsync();
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
