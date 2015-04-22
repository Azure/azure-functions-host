// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageAccountProvider : IStorageAccountProvider
    {
        public IStorageAccount StorageAccount { get; set; }

        public IStorageAccount DashboardAccount { get; set; }

        public Task<IStorageAccount> GetAccountAsync(string connectionStringName, CancellationToken cancellationToken)
        {
            IStorageAccount account;

            if (connectionStringName == ConnectionStringNames.Storage)
            {
                account = StorageAccount;
            }
            else if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                account = DashboardAccount;
            }
            else
            {
                account = null;
            }

            return Task.FromResult(account);
        }
    }
}
