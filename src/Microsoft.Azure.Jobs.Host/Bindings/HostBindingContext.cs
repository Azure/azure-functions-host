using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class HostBindingContext
    {
        private readonly IBindingProvider _bindingProvider;
        private readonly INotifyNewBlob _notifyNewBlob;
        private readonly CancellationToken _cancellationToken;
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;

        public HostBindingContext(
            IBindingProvider bindingProvider,
            INotifyNewBlob notifyNewBlob,
            CancellationToken cancellationToken,
            INameResolver nameResolver,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString)
        {
            _bindingProvider = bindingProvider;
            _notifyNewBlob = notifyNewBlob;
            _cancellationToken = cancellationToken;
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public INotifyNewBlob NotifyNewBlob
        {
            get { return _notifyNewBlob; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
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
    }
}
