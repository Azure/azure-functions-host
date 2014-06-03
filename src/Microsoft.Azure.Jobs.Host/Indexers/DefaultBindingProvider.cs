using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.Cancellation;
using Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput;
using Microsoft.Azure.Jobs.Host.Bindings.Data;
using Microsoft.Azure.Jobs.Host.Bindings.Runtime;
using Microsoft.Azure.Jobs.Host.Bindings.StorageAccount;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Queues.Bindings;
using Microsoft.Azure.Jobs.Host.Tables;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal static class DefaultBindingProvider
    {
        public static IBindingProvider Create(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            List<IBindingProvider> innerProviders = new List<IBindingProvider>();
            innerProviders.Add(new QueueAttributeBindingProvider());
            innerProviders.Add(new BlobAttributeBindingProvider(cloudBlobStreamBinderTypes));

            innerProviders.Add(new TableAttributeBindingProvider());

            Type serviceBusProviderType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.Jobs.ServiceBus.Bindings.ServiceBusAttributeBindingProvider");

            if (serviceBusProviderType != null)
            {
                IBindingProvider serviceBusAttributeBindingProvider =
                    (IBindingProvider)Activator.CreateInstance(serviceBusProviderType);
                innerProviders.Add(serviceBusAttributeBindingProvider);
            }

            innerProviders.Add(new CloudStorageAccountBindingProvider());
            innerProviders.Add(new CancellationTokenBindingProvider());

            // The console output binder below will handle all remaining TextWriter parameters. It must come after the
            // Blob binding provider; otherwise bindings like Do([Blob("a/b")] TextWriter blob) wouldn't work.
            innerProviders.Add(new ConsoleOutputBindingProvider());

            innerProviders.Add(new RuntimeBindingProvider());
            innerProviders.Add(new DataBindingProvider());

            return new CompositeBindingProvider(innerProviders);
        }

    }
}
