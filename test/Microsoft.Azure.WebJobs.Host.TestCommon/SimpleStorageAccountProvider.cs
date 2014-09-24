// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class SimpleStorageAccountProvider : IStorageAccountProvider
    {
        public CloudStorageAccount StorageAccount { get; set; }

        public CloudStorageAccount DashboardAccount { get; set; }

        IStorageAccount IStorageAccountProvider.GetAccount(string connectionStringName)
        {
            if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                return DashboardAccount != null ? new StorageAccount(DashboardAccount) : null;
            }
            else if (connectionStringName == ConnectionStringNames.Storage)
            {
                return StorageAccount != null ? new StorageAccount(StorageAccount) : null;
            }
            else
            {
                return null;
            }
        }
    }
}
