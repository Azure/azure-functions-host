// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
            TestJobHostConfiguration configuration = new TestJobHostConfiguration
            {
                TypeLocator = new FakeTypeLocator(typeof(TProgram)),
                StorageAccountProvider = new SimpleStorageAccountProvider
                {
                    StorageAccount = storageAccount,
                    // use null logging string since unit tests don't need logs.
                    DashboardAccount = null
                },
                StorageCredentialsValidator = new NullStorageCredentialsValidator(),
                ConnectionStringProvider = new NullConnectionStringProvider(),
                Queues = new SimpleQueueConfiguration(maxDequeueCount)
            };

            return new TestJobHost<TProgram>(configuration);
        }
    }
}
