using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal interface IListenerFactory
    {
        IListener Create(IFunctionExecutor executor, ListenerFactoryContext context);
    }
}
