using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.FunctionsHost.Grpc;

internal class GrpcWorkerChannel : IInternalWorkerChannel, IAsyncDisposable
{
    private readonly BidirectionalChannel _channel = new();

    public ChannelReader<MessageToWorker> WorkerMessageReader => _channel.WorkerMessageReader;

    public ChannelWriter<MessageFromWorker> HostMessageWriter => _channel.HostMessageWriter;

    public ChannelReader<MessageFromWorker> HostMessageReader => _channel.HostMessageReader;

    public ChannelWriter<MessageToWorker> WorkerMessageWriter => _channel.WorkerMessageWriter;

    public async IAsyncEnumerable<MessageFromWorker> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.HostMessageReader.WaitToReadAsync(cancellationToken))
        {
            while (!cancellationToken.IsCancellationRequested && _channel.HostMessageReader.TryRead(out var message))
            {
                yield return message;
            }
        }
    }

    public bool TryWrite(MessageToWorker message)
    {
        return _channel.WorkerMessageWriter.TryWrite(message);
    }

    public async ValueTask DisposeAsync()
    {
        // this signals upstream that we are done writing messages
        _channel.HostMessageWriter.Complete();
        _channel.WorkerMessageWriter.Complete();
        await _channel.HostMessageReader.Completion;
        await _channel.WorkerMessageReader.Completion;
    }
}

// a class to hold the endpoints of our bidirectional channels
internal class BidirectionalChannel : IInternalWorkerChannel, IExternalWorkerChannel
{
    // for messages going from Worker -> Host
    private readonly Channel<MessageFromWorker> _hostMessageChannel = Channel.CreateUnbounded<MessageFromWorker>();

    // for messages going from Host -> Worker
    private readonly Channel<MessageToWorker> _workerMessageChannel = Channel.CreateUnbounded<MessageToWorker>();

    public ChannelReader<MessageToWorker> WorkerMessageReader => _workerMessageChannel.Reader;

    public ChannelWriter<MessageToWorker> WorkerMessageWriter => _workerMessageChannel.Writer;

    public ChannelReader<MessageFromWorker> HostMessageReader => _hostMessageChannel.Reader;

    public ChannelWriter<MessageFromWorker> HostMessageWriter => _hostMessageChannel.Writer;
}

//internal class ChannelRouter(BidirectionalChannel sourceChannel) : IWorkerChannelFactory, IWorkerChannelWriterProvider
//{
//    private readonly BidirectionalChannel _sourceChannel = sourceChannel ?? throw new ArgumentNullException(nameof(sourceChannel));

//    // Every connection can have two JobHost workers attached in it's lifetime.
//    //   Either it goes Placeholder -> Specialized
//    //   Or it is Specialized only.
//    private readonly GrpcWorkerChannel? _placeholderChannel;
//    private readonly GrpcWorkerChannel? _specializedChannel;

//    private Task? _routingTask;

//    public void Start()
//    {
//        _routingTask = StartRoutingAsync();
//    }

//    private async Task StartRoutingAsync()
//    {
//        // Chain messages for the host from the source into the appropriate JobHost worker
//        while (await _sourceChannel.HostMessageReader.WaitToReadAsync())
//        {
//            while (_sourceChannel.HostMessageReader.TryRead(out var message))
//            {
//                // TODO: determine which channel to route to
//                if (_specializedChannel is not null &&
//                    !_specializedChannel.HostMessageWriter.TryWrite(message))
//                {
//                    // handle failure to write, e.g., channel is full or closed
//                }
//            }
//        }
//    }

//    public IWorkerChannel CreateWorkerChannel(string workerId)
//    {
//        var channel = CreateChannel(workerId);
//    }

//    private GrpcWorkerChannel CreateChannel(string applicationId)
//    {
//        var channel = new GrpcWorkerChannel();

//        //_appMap[applicationId] = channel;

//        // forward messages back to the source channel
//        _ = Task.Run(async () =>
//        {
//            while (await channel.WorkerMessageReader.WaitToReadAsync())
//            {
//                while (channel.WorkerMessageReader.TryRead(out var message))
//                {
//                    // route to worker
//                    _sourceChannel.WorkerMessageWriter.TryWrite(message);
//                }
//            }
//        });

//        return channel;
//    }

//    public IWorkerChannelWriter GetWriter(string applicationId)
//    {
//        if (!_appMap.TryGetValue(applicationId, out var channel))
//        {
//            channel = CreateChannel(applicationId);
//        }
//        // TODO: I don't like this...
//        return channel;
//    }

//    private class DisposableChannel(IWorkerChannel source, Action onDispose) : IWorkerChannel, IAsyncDisposable
//    {
//        private readonly Action _onDispose = onDispose;
//        private readonly IWorkerChannel _source = source;

//        public IAsyncEnumerable<MessageFromWorker> ReadAsync(CancellationToken cancellationToken) => _source.ReadAsync(cancellationToken);

//        public bool TryWrite(MessageToWorker message) => _source.TryWrite(message);

//        public ValueTask DisposeAsync()
//        {
//            _onDispose();

//            if (_source is IAsyncDisposable asyncDisposable)
//            {
//                return asyncDisposable.DisposeAsync();
//            }

//            return ValueTask.CompletedTask;
//        }
//    }
//}
