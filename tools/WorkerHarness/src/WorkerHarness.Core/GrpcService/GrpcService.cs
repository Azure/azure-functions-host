// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Threading.Channels;

namespace WorkerHarness.Core.GrpcService
{
    public sealed class GrpcService : FunctionRpc.FunctionRpcBase
    {
        private readonly Channel<StreamingMessage> _inboundChannel;

        private readonly Channel<StreamingMessage> _outboundChannel;

        public GrpcService(Channel<StreamingMessage> inboundChannel, Channel<StreamingMessage> outboundChannel)
        {
            _inboundChannel = inboundChannel;
            _outboundChannel = outboundChannel;
        }

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            // send message from the application to the responseStream
            _ = Task.Run(() => SendOutgoingGrpcMessages(responseStream));

            // extract message from the requestStream and send it to the application
            while (await requestStream.MoveNext())
            {
                await _inboundChannel.Writer.WriteAsync(requestStream.Current);
            }
        }

        private async void SendOutgoingGrpcMessages(IServerStreamWriter<StreamingMessage> responseStream)
        {
            while (true)
            {
                StreamingMessage message = await _outboundChannel.Reader.ReadAsync();
                await responseStream.WriteAsync(message);
            }
        }
    }
}
