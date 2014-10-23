// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public static class JobHostFactory
    {
        public static TestJobHost<TProgram> Create<TProgram>()
        {
            return Create<TProgram>(CloudStorageAccount.DevelopmentStorageAccount, maxDequeueCount: 5);
        }

        public static TestJobHost<TProgram> Create<TProgram>(int maxDequeueCount)
        {
            return Create<TProgram>(CloudStorageAccount.DevelopmentStorageAccount, maxDequeueCount);
        }

        public static TestJobHost<TProgram> Create<TProgram>(CloudStorageAccount storageAccount)
        {
            return Create<TProgram>(storageAccount, maxDequeueCount : 5);
        }

        public static TestJobHost<TProgram> Create<TProgram>(CloudStorageAccount storageAccount, int maxDequeueCount)
        {
            IStorageAccountProvider storageAccountProvider = new SimpleStorageAccountProvider
            {
                StorageAccount = storageAccount,
                // use null logging string since unit tests don't need logs.
                DashboardAccount = null
            };
            IServiceBusAccountProvider serviceBusAccountProvider = new NullServiceBusAccountProvider();
            IExtensionTypeLocator extensionTypeLocator = new NullExtensionTypeLocator();
            IHostIdProvider hostIdProvider = new FixedHostIdProvider("test");
            INameResolver nameResolver = null;

            TestJobHostConfiguration configuration = new TestJobHostConfiguration
            {
                FunctionIndexProvider = new FunctionIndexProvider(new FakeTypeLocator(typeof(TProgram)),
                    DefaultTriggerBindingProvider.Create(nameResolver, storageAccountProvider,
                    serviceBusAccountProvider, extensionTypeLocator, hostIdProvider),
                    DefaultBindingProvider.Create(nameResolver, storageAccountProvider, serviceBusAccountProvider,
                    extensionTypeLocator)),
                StorageAccountProvider = storageAccountProvider,
                ServiceBusAccountProvider = serviceBusAccountProvider,
                Queues = new SimpleQueueConfiguration(maxDequeueCount)
            };

            return new TestJobHost<TProgram>(configuration);
        }
    }
}
