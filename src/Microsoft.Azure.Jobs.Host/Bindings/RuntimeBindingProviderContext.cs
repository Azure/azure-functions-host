using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class RuntimeBindingProviderContext
    {
        public IBindingProvider BindingProvider { get; set; }

        public INotifyNewBlob NotifyNewBlob { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public TextWriter ConsoleOutput { get; set; }

        public INameResolver NameResolver { get; set; }

        public CloudStorageAccount StorageAccount { get; set; }

        public string ServiceBusConnectionString { get; set; }
    }
}
