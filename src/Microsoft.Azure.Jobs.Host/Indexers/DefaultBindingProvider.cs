// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Cancellation;
using Microsoft.Azure.WebJobs.Host.Bindings.ConsoleOutput;
using Microsoft.Azure.WebJobs.Host.Bindings.Data;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Host.Bindings.StorageAccount;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.Azure.WebJobs.Host.Queues.Bindings;
using Microsoft.Azure.WebJobs.Host.Tables;

namespace Microsoft.Azure.WebJobs.Host.Indexers
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
                "Microsoft.Azure.WebJobs.ServiceBus.Bindings.ServiceBusAttributeBindingProvider");

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
