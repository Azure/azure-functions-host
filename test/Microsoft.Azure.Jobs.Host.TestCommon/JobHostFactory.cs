// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public static class JobHostFactory
    {
        public static TestJobHost<TProgram> Create<TProgram>()
        {
            return Create<TProgram>(CloudStorageAccount.DevelopmentStorageAccount);
        }

        public static TestJobHost<TProgram> Create<TProgram>(CloudStorageAccount storageAccount)
        {
            TestJobHostConfiguration configuration = new TestJobHostConfiguration
            {
                TypeLocator = new SimpleTypeLocator(typeof(TProgram)),
                StorageAccountProvider = new SimpleStorageAccountProvider
                {
                    StorageAccount = storageAccount,
                    // use null logging string since unit tests don't need logs.
                    DashboardAccount = null
                },
                StorageCredentialsValidator = new NullStorageCredentialsValidator(),
                ConnectionStringProvider = new NullConnectionStringProvider()
            };

            return new TestJobHost<TProgram>(configuration);
        }
    }
}
