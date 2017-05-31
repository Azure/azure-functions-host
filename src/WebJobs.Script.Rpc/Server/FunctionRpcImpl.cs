using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using System.Reactive.Subjects;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class FunctionRpcImpl : FunctionRpc.FunctionRpcBase
    {
        private Subject<ChannelContext> _connections = new Subject<ChannelContext>();

        public IObservable<ChannelContext> Connections => _connections;

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            var startStream = requestStream.Current.Content.Unpack<StartStream>();
            var input = new Subject<StreamingMessage>();
            var output = new Subject<StreamingMessage>();

            output.Subscribe(msg => responseStream.WriteAsync(msg));

            _connections.OnNext(new ChannelContext
            {
                WorkerId = startStream.WorkerId,
                RequestId = requestStream.Current.RequestId,
                InputStream = input,
                OutputStream = output
            });

            while (await requestStream.MoveNext(CancellationToken.None))
            {
                input.OnNext(requestStream.Current);
            }
        }
    }
}
