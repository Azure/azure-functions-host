// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal static class StorageAccountProviderExtensions
    {
        public static Task<IStorageAccount> GetDashboardAccountAsync(this IStorageAccountProvider provider,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            return provider.GetAccountAsync(ConnectionStringNames.Dashboard, cancellationToken);
        }

        public static Task<IStorageAccount> GetStorageAccountAsync(this IStorageAccountProvider provider,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            return provider.GetAccountAsync(ConnectionStringNames.Storage, cancellationToken);
        }
    }
}
