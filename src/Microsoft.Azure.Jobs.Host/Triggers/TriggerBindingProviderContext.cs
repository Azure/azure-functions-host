using System.Reflection;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggerBindingProviderContext
    {
        public ParameterInfo Parameter { get; set; }

        public INameResolver NameResolver { get; set; }

        public CloudStorageAccount StorageAccount { get; set; }

        public string ServiceBusConnectionString { get; set; }

        public string Resolve(string input)
        {
            return NameResolver.ResolveWholeString(input);
        }
    }
}
