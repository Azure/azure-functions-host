using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionDefinition : IFunctionDefinition
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly IFunctionBinding _binding;
        private readonly IListenerFactory _listenerFactory;
        private readonly MethodInfo _methodInfo;

        public FunctionDefinition(FunctionDescriptor descriptor, IFunctionBinding binding,
            IListenerFactory listenerFactory, MethodInfo methodInfo)
        {
            _descriptor = descriptor;
            _binding = binding;
            _listenerFactory = listenerFactory;
            _methodInfo = methodInfo;
        }

        public FunctionDescriptor Descriptor
        {
            get { return _descriptor; }
        }

        public IFunctionBinding Binding
        {
            get { return _binding; }
        }

        public IListenerFactory ListenerFactory
        {
            get { return _listenerFactory; }
        }

        // How to bind the parameters. Will eventually be encapsulated behind Executor & Listener properties.
        public MethodInfo Method
        {
            get { return _methodInfo; }
        }
        public string TriggerParameterName { get; set; }
        public ITriggerBinding TriggerBinding { get; set; }
        public IReadOnlyDictionary<string, IBinding> NonTriggerBindings { get; set; }
    }
}
