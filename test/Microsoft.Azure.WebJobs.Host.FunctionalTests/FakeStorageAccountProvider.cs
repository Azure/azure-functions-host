// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal class FakeStorageAccountProvider : IStorageAccountProvider
    {
        public IStorageAccount StorageAccount { get; set; }

        public IStorageAccount DashboardAccount { get; set; }

        public IStorageAccount GetAccount(string connectionStringName)
        {
            if (connectionStringName == ConnectionStringNames.Storage)
            {
                return StorageAccount;
            }
            else if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                return DashboardAccount;
            }
            else
            {
                return null;
            }
        }
    }
}
