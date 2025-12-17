using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.FunctionsHost.Grpc;

internal interface IMessageHandler
{
    ValueTask<bool> HandleMessage(MessageFromWorker message);
}
