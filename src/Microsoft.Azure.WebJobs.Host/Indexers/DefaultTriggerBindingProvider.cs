// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs.Triggers;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Queues.Triggers;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal static class DefaultTriggerBindingProvider
    {
        public static ITriggerBindingProvider Create(IStorageAccountProvider storageAccountProvider,
            IServiceBusAccountProvider serviceBusAccountProvider, IExtensionTypeLocator extensionTypeLocator,
            IHostIdProvider hostIdProvider)
        {
            List<ITriggerBindingProvider> innerProviders = new List<ITriggerBindingProvider>();
            innerProviders.Add(new QueueTriggerAttributeBindingProvider(storageAccountProvider));
            innerProviders.Add(new BlobTriggerAttributeBindingProvider(storageAccountProvider, extensionTypeLocator,
                hostIdProvider));

            Type serviceBusProviderType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.WebJobs.ServiceBus.Triggers.ServiceBusTriggerAttributeBindingProvider");

            if (serviceBusProviderType != null)
            {
                ITriggerBindingProvider serviceBusAttributeBindingProvider =
                    (ITriggerBindingProvider)Activator.CreateInstance(serviceBusProviderType,
                    serviceBusAccountProvider);
                innerProviders.Add(serviceBusAttributeBindingProvider);
            }

            return new CompositeTriggerBindingProvider(innerProviders);
        }
    }
}
