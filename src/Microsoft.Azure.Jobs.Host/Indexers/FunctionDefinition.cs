using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionDefinition : IFunctionDefinition
    {
        private readonly IFunctionInstanceFactory _instanceFactory;
        private readonly IListenerFactory _listenerFactory;

        public FunctionDefinition(IFunctionInstanceFactory instanceFactory, IListenerFactory listenerFactory)
        {
            _instanceFactory = instanceFactory;
            _listenerFactory = listenerFactory;
        }

        public IFunctionInstanceFactory InstanceFactory
        {
            get { return _instanceFactory; }
        }

        public IListenerFactory ListenerFactory
        {
            get { return _listenerFactory; }
        }
    }
}
