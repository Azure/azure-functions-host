// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
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
            return Create<TProgram>(storageAccount, maxDequeueCount: 5);
        }

        public static TestJobHost<TProgram> Create<TProgram>(CloudStorageAccount storageAccount, int maxDequeueCount)
        {
            IHostIdProvider hostIdProvider = new FixedHostIdProvider("test");
            JobHostConfiguration config = TestHelpers.NewConfig<TProgram>(hostIdProvider);

            IStorageAccountProvider storageAccountProvider = new SimpleStorageAccountProvider(config)
            {
                StorageAccount = storageAccount,
                // use null logging string since unit tests don't need logs.
                DashboardAccount = null
            };
            config.AddServices(storageAccountProvider);
            return new TestJobHost<TProgram>(config);
        }
    }
}
