namespace OutOfProcModel.FunctionsHost.Grpc;

//internal class GrpcFunctionMetadataFactory : IFunctionMetadataFactory, IMessageHandler
//{
//    private readonly string _applicationId;
//    private readonly IWorkerChannelWriter _channelWriterProvider;

//    private readonly TaskCompletionSource<IEnumerable<string>> _metadataTaskCompletionSource = new();
//    private readonly Lazy<Task<IEnumerable<string>>> _metadataLazy;

//    public GrpcFunctionMetadataFactory(string applicationId, IWorkerChannelWriterProvider channelWriterProvider)
//    {
//        _applicationId = applicationId;
//        _channelWriterProvider = channelWriterProvider.GetWriter(applicationId);
//        _metadataLazy = new(LoadMetadataAsync);
//    }

//    public Task<IEnumerable<string>> GetFunctionMetadataAsync()
//    {
//        return _metadataLazy.Value;
//    }

//    private Task<IEnumerable<string>> LoadMetadataAsync()
//    {
//        _channelWriterProvider.TryWrite(new MessageToWorker(new StreamingMessage()));
//        return _metadataTaskCompletionSource.Task;
//    }

//    public ValueTask<bool> HandleMessage(MessageFromWorker message)
//    {
//        if (message.Message.ContentCase == StreamingMessage.ContentOneofCase.FunctionMetadataResponse)
//        {
//            _metadataTaskCompletionSource.TrySetResult(message.Message.FunctionMetadataResponse.FunctionMetadataResults.Select(p => p.Name)); // mocking this
//            return ValueTask.FromResult(true);
//        }

//        return ValueTask.FromResult(false);
//    }
//}
