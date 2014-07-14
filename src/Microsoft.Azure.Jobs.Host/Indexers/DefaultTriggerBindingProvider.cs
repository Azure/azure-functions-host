using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal static class DefaultTriggerBindingProvider
    {
        public static ITriggerBindingProvider Create(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            List<ITriggerBindingProvider> innerProviders = new List<ITriggerBindingProvider>();
            innerProviders.Add(new QueueTriggerAttributeBindingProvider());
            innerProviders.Add(new BlobTriggerAttributeBindingProvider(cloudBlobStreamBinderTypes));

            Type serviceBusProviderType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.Jobs.ServiceBus.Triggers.ServiceBusTriggerAttributeBindingProvider");

            if (serviceBusProviderType != null)
            {
                ITriggerBindingProvider serviceBusAttributeBindingProvider =
                    (ITriggerBindingProvider)Activator.CreateInstance(serviceBusProviderType);
                innerProviders.Add(serviceBusAttributeBindingProvider);
            }

            return new CompositeTriggerBindingProvider(innerProviders);
        }
    }
}
