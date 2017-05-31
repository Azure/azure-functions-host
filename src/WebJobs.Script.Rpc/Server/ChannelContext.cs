using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;
using System.Reactive.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    using StreamTypes = StreamingMessage.Types.Type;

    public class ChannelContext
    {
        public IObservable<StreamingMessage> InputStream { get; set; }

        public IObserver<StreamingMessage> OutputStream { get; set; }

        public string RequestId { get; set; }

        public string WorkerId { get; set; }

        public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, TimeSpan? timeout = null) 
            where TRequest : IMessage
            where TResponse : IMessage, new()
        {
            // build request id
            string requestId = Guid.NewGuid().ToString();

            // build streaming message
            StreamingMessage streamingMessage = new StreamingMessage()
            {
                RequestId = requestId,
                Type = _typeMap[typeof(TRequest)],
                Content = Any.Pack(request)
            };

            // send streaming message
            OutputStream.OnNext(streamingMessage);

            TaskCompletionSource<TResponse> responseSource = new TaskCompletionSource<TResponse>();

            IDisposable subscription = null;

            // create request subscription
            // TODO: timeouts
            subscription = InputStream
                .Where(msg => msg.RequestId == requestId)
                .Timeout(timeout ?? TimeSpan.FromSeconds(10))
                .Subscribe(msg =>
                {
                    TResponse response = msg.Content.Unpack<TResponse>();
                    responseSource.SetResult(response);
                    subscription?.Dispose();
                }, err =>
                {
                    responseSource.SetException(err);
                    subscription?.Dispose();
                });

            return responseSource.Task;
        }

        // TODO: remove enum? Any's type urls sufficient?
        private static IDictionary<System.Type, StreamTypes> _typeMap = new Dictionary<System.Type, StreamTypes>()
        {
            [typeof(StartStream)] = StreamTypes.StartStream,
            [typeof(WorkerInitRequest)] = StreamTypes.WorkerInitRequest,
            [typeof(WorkerInitResponse)] = StreamTypes.WorkerInitResponse,
            [typeof(WorkerHeartbeat)] = StreamTypes.WorkerHeartbeat,
            [typeof(WorkerTerminate)] = StreamTypes.WorkerTerminate,
            [typeof(WorkerStatusRequest)] = StreamTypes.WorkerStatusRequest,
            [typeof(WorkerStatusResponse)] = StreamTypes.WorkerStatusResponse,
            [typeof(FileChangeEventRequest)] = StreamTypes.FileChangeEventRequest,
            [typeof(FileChangeEventResponse)] = StreamTypes.FileChangeEventResponse,
            [typeof(FunctionLoadRequest)] = StreamTypes.FunctionLoadRequest,
            [typeof(FunctionLoadResponse)] = StreamTypes.FunctionLoadResponse,
            [typeof(InvocationRequest)] = StreamTypes.InvocationRequest,
            [typeof(InvocationResponse)] = StreamTypes.InvocationResponse,
            [typeof(InvocationCancel)] = StreamTypes.InvocationCancel,
            [typeof(RpcLog)] = StreamTypes.RpcLog
        };
    }
}
