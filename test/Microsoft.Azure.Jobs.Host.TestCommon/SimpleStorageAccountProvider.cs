// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class SimpleStorageAccountProvider : IStorageAccountProvider
    {
        public CloudStorageAccount StorageAccount { get; set; }

        public CloudStorageAccount DashboardAccount { get; set; }

        public CloudStorageAccount GetAccount(string connectionStringName)
        {
            if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                return DashboardAccount;
            }
            else if (connectionStringName == ConnectionStringNames.Storage)
            {
                return StorageAccount;
            }
            else
            {
                return null;
            }
        }
    }
}
