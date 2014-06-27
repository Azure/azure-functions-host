using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionIndexContext
    {
        private readonly ITypeLocator _typeLocator;
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;

        public FunctionIndexContext(
            ITypeLocator typeLocator,
            INameResolver nameResolver,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString)
        {
            _typeLocator = typeLocator;
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
        }

        public ITypeLocator TypeLocator
        {
            get { return _typeLocator; }
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _storageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _serviceBusConnectionString; }
        }

        public string Resolve(string input)
        {
            if (_nameResolver == null)
            {
                return input;
            }

            return _nameResolver.ResolveWholeString(input);
        }
    }
}
