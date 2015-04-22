// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class SimpleStorageAccountProvider : IStorageAccountProvider
    {
        public CloudStorageAccount StorageAccount { get; set; }

        public CloudStorageAccount DashboardAccount { get; set; }

        Task<IStorageAccount> IStorageAccountProvider.GetAccountAsync(string connectionStringName,
            CancellationToken cancellationToken)
        {
            IStorageAccount account;

            if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                account = DashboardAccount != null ? new StorageAccount(DashboardAccount) : null;
            }
            else if (connectionStringName == ConnectionStringNames.Storage)
            {
                account = StorageAccount != null ? new StorageAccount(StorageAccount) : null;
            }
            else
            {
                account = null;
            }

            return Task.FromResult(account);
        }
    }
}
