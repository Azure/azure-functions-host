using System.Threading.Channels;

namespace OutOfProcModel.Abstractions.Worker;

internal interface IWorkerChannel
{
    ChannelReader<MessageFromWorker> HostMessageReader { get; }

    ChannelWriter<MessageToWorker> WorkerMessageWriter { get; }
}

internal interface IExternalWorkerChannel
{
    ChannelReader<MessageToWorker> WorkerMessageReader { get; }

    ChannelWriter<MessageFromWorker> HostMessageWriter { get; }
}
