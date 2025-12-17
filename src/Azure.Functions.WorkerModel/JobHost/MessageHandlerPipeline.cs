using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.FunctionsHost.Grpc;

internal class MessageHandlerPipeline
{
    private readonly List<IMessageHandler> _messageHandlers = [];

    public MessageHandlerPipeline(IEnumerable<IMessageHandler> registeredMessageHandlers)
    {
        _messageHandlers.AddRange(registeredMessageHandlers);
    }

    public async ValueTask<bool> HandleMessage(MessageFromWorker message)
    {
        foreach (var handler in _messageHandlers)
        {
            if (await handler.HandleMessage(message))
            {
                return true;
            }
        }

        return false;
    }
}
