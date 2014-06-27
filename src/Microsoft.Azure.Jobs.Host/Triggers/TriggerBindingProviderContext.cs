using System.Reflection;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggerBindingProviderContext
    {
        private readonly FunctionIndexerContext _indexerContext;
        private readonly ParameterInfo _parameter;

        public TriggerBindingProviderContext(FunctionIndexerContext indexerContext, ParameterInfo parameter)
        {
            _indexerContext = indexerContext;
            _parameter = parameter;
        }

        public INameResolver NameResolver
        {
            get { return _indexerContext.NameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _indexerContext.StorageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _indexerContext.ServiceBusConnectionString; }
        }

        public ParameterInfo Parameter
        {
            get { return _parameter; }
        }

        public string Resolve(string input)
        {
            return _indexerContext.Resolve(input);
        }
    }
}
