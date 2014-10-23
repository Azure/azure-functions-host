// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal static class FunctionIndexerFactory
    {
        public static FunctionIndexer Create(CloudStorageAccount account, INameResolver nameResolver = null)
        {
            IStorageAccount storageAccount = account != null ? new StorageAccount(account) : null;
            IStorageAccountProvider storageAccountProvider = new SimpleStorageAccountProvider
            {
                StorageAccount = account
            };
            IServiceBusAccountProvider serviceBusAccountProvider = null;
            IExtensionTypeLocator extensionTypeLocator = new NullExtensionTypeLocator();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, serviceBusAccountProvider, extensionTypeLocator,
                new FixedHostIdProvider("test"), new SimpleQueueConfiguration(maxDequeueCount: 5),
                BackgroundExceptionDispatcher.Instance);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(nameResolver, storageAccountProvider,
                serviceBusAccountProvider, extensionTypeLocator);

            return new FunctionIndexer(triggerBindingProvider, bindingProvider);
        }
    }
}
