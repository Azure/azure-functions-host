// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Blobs.Triggers;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Triggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal static class DefaultTriggerBindingProvider
    {
        public static ITriggerBindingProvider Create(INameResolver nameResolver,
            IStorageAccountProvider storageAccountProvider,
            IServiceBusAccountProvider serviceBusAccountProvider,
            IExtensionTypeLocator extensionTypeLocator,
            IHostIdProvider hostIdProvider,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IContextSetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherSetter,
            IContextSetter<IBlobWrittenWatcher> blobWrittenWatcherSetter,
            ISharedContextProvider sharedContextProvider,
            TextWriter log)
        {
            List<ITriggerBindingProvider> innerProviders = new List<ITriggerBindingProvider>();
            innerProviders.Add(new QueueTriggerAttributeBindingProvider(nameResolver, storageAccountProvider,
                queueConfiguration, backgroundExceptionDispatcher, messageEnqueuedWatcherSetter,
                sharedContextProvider, log));
            innerProviders.Add(new BlobTriggerAttributeBindingProvider(nameResolver, storageAccountProvider,
                extensionTypeLocator, hostIdProvider, queueConfiguration, backgroundExceptionDispatcher,
                blobWrittenWatcherSetter, messageEnqueuedWatcherSetter, sharedContextProvider, log));

            Type serviceBusProviderType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.WebJobs.ServiceBus.Triggers.ServiceBusTriggerAttributeBindingProvider");

            if (serviceBusProviderType != null)
            {
                ITriggerBindingProvider serviceBusAttributeBindingProvider =
                    (ITriggerBindingProvider)Activator.CreateInstance(serviceBusProviderType, nameResolver,
                    serviceBusAccountProvider);
                innerProviders.Add(serviceBusAttributeBindingProvider);
            }

            return new CompositeTriggerBindingProvider(innerProviders);
        }
    }
}
