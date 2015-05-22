// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Cancellation;
using Microsoft.Azure.WebJobs.Host.Bindings.ConsoleOutput;
using Microsoft.Azure.WebJobs.Host.Bindings.Data;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Host.Bindings.StorageAccount;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Bindings;
using Microsoft.Azure.WebJobs.Host.Tables;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal static class DefaultBindingProvider
    {
        public static IBindingProvider Create(INameResolver nameResolver,
            IStorageAccountProvider storageAccountProvider,
            IExtensionTypeLocator extensionTypeLocator,
            IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter,
            IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter,
            IExtensionRegistry extensions)
        {
            List<IBindingProvider> innerProviders = new List<IBindingProvider>();
            innerProviders.Add(new QueueAttributeBindingProvider(nameResolver, storageAccountProvider, messageEnqueuedWatcherGetter));
            innerProviders.Add(new BlobAttributeBindingProvider(nameResolver, storageAccountProvider, extensionTypeLocator, blobWrittenWatcherGetter));
            innerProviders.Add(new TableAttributeBindingProvider(nameResolver, storageAccountProvider));

            // add any registered extension binding providers
            foreach (IBindingProvider provider in extensions.GetExtensions(typeof(IBindingProvider)))
            {
                innerProviders.Add(provider);
            }

            innerProviders.Add(new CloudStorageAccountBindingProvider(storageAccountProvider));
            innerProviders.Add(new CancellationTokenBindingProvider());

            // The console output binder below will handle all remaining TextWriter parameters. It must come after the
            // Blob binding provider; otherwise bindings like Do([Blob("a/b")] TextWriter blob) wouldn't work.
            innerProviders.Add(new ConsoleOutputBindingProvider());

            ContextAccessor<IBindingProvider> bindingProviderAccessor = new ContextAccessor<IBindingProvider>();
            innerProviders.Add(new RuntimeBindingProvider(bindingProviderAccessor));
            innerProviders.Add(new DataBindingProvider());

            IBindingProvider bindingProvider = new CompositeBindingProvider(innerProviders);
            bindingProviderAccessor.SetValue(bindingProvider);
            return bindingProvider;
        }
    }
}
