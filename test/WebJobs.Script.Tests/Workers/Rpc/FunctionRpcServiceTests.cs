// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class FunctionRpcServiceTests
    {
        [Fact]
        public async Task EventStream_TransfersStreamingMessagesDirectly()
        {
            const string workerId = "worker-id";
            var channelRegistry = new ServerDuplexChannelRegistry();
            await using DuplexChannel<StreamingMessage> channelLease = channelRegistry.CreateLease(workerId);
            var service = new FunctionRpcService(channelRegistry, NullLogger<FunctionRpcService>.Instance);
            var requestStream = new TestAsyncStreamReader<StreamingMessage>();
            var responseStream = new TestServerStreamWriter<StreamingMessage>();
            var context = new Mock<ServerCallContext>();

            var startStreamMessage = new StreamingMessage
            {
                StartStream = new StartStream
                {
                    WorkerId = workerId,
                },
            };
            await requestStream.WriteAsync(startStreamMessage);

            Task eventStreamTask = service.EventStream(requestStream, responseStream, context.Object);

            StreamingMessage receivedStartStream = await channelLease.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(startStreamMessage, receivedStartStream);

            var inboundMessage = new StreamingMessage
            {
                WorkerInitResponse = new WorkerInitResponse(),
            };
            await requestStream.WriteAsync(inboundMessage);

            StreamingMessage receivedInbound = await channelLease.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(inboundMessage, receivedInbound);

            var outboundMessage = new StreamingMessage
            {
                WorkerInitRequest = new WorkerInitRequest(),
            };
            await channelLease.Writer.WriteAsync(outboundMessage);

            StreamingMessage receivedOutbound = await responseStream.ReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(outboundMessage, receivedOutbound);

            requestStream.Complete();
            await eventStreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private sealed class TestAsyncStreamReader<T> : IAsyncStreamReader<T>
        {
            private readonly Channel<T> _messages = Channel.CreateUnbounded<T>();

            public T Current { get; private set; }

            public void Complete() => _messages.Writer.TryComplete();

            public ValueTask WriteAsync(T message) => _messages.Writer.WriteAsync(message);

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                if (await _messages.Reader.WaitToReadAsync(cancellationToken) && _messages.Reader.TryRead(out T message))
                {
                    Current = message;
                    return true;
                }

                return false;
            }
        }

        private sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
        {
            private readonly Channel<T> _messages = Channel.CreateUnbounded<T>();

            public WriteOptions WriteOptions { get; set; }

            public Task WriteAsync(T message) => _messages.Writer.WriteAsync(message).AsTask();

            public Task<T> ReadAsync() => _messages.Reader.ReadAsync().AsTask();
        }
    }
}
