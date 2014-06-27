using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionIndexerContext
    {
        private readonly FunctionIndexContext _indexContext;
        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;

        private FunctionIndexerContext(FunctionIndexContext indexContext,
            ITriggerBindingProvider triggerBindingProvider,
            IBindingProvider bindingProvider)
        {
            _indexContext = indexContext;
            _triggerBindingProvider = triggerBindingProvider;
            _bindingProvider = bindingProvider;
        }

        public INameResolver NameResolver
        {
            get { return _indexContext.NameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _indexContext.StorageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _indexContext.ServiceBusConnectionString; }
        }

        public ITriggerBindingProvider TriggerBindingProvider
        {
            get { return _triggerBindingProvider; }
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public string Resolve(string input)
        {
            return _indexContext.Resolve(input);
        }

        public static FunctionIndexerContext CreateDefault(FunctionIndexContext indexContext,
            IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(cloudBlobStreamBinderTypes);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(cloudBlobStreamBinderTypes);
            return new FunctionIndexerContext(indexContext, triggerBindingProvider, bindingProvider);
        }
    }
}
