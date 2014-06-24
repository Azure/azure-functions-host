using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggerClient<TTriggerValue> : ITriggerClient
    {
        private readonly ITriggeredFunctionBinding<TTriggerValue> _functionBinding;
        private readonly IListenerFactory _listenerFactory;

        public TriggerClient(ITriggeredFunctionBinding<TTriggerValue> functionBinding, IListenerFactory listenerFactory)
        {
            _functionBinding = functionBinding;
            _listenerFactory = listenerFactory;
        }

        public IFunctionBinding FunctionBinding
        {
            get { return _functionBinding; }
        }

        public IListenerFactory ListenerFactory
        {
            get { return _listenerFactory; }
        }
    }
}
