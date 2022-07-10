using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class GrpcService : FunctionRpc.FunctionRpcBase
    {
        private Channel<StreamingMessage> _inboundChannel;

        private Channel<StreamingMessage> _outboundChannel;

        public GrpcService(Channel<StreamingMessage> inboundChannel, Channel<StreamingMessage> outboundChannel)
        {
            _outboundChannel = outboundChannel;
            _inboundChannel = inboundChannel;
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
