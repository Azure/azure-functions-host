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
        public static Task<IStorageAccount> GetDashboardAccountAsync(this IStorageAccountProvider provider, CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            return provider.TryGetAccountAsync(ConnectionStringNames.Dashboard, cancellationToken);
        }

        public static async Task<IStorageAccount> GetStorageAccountAsync(this IStorageAccountProvider provider, CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            IStorageAccount account = await provider.TryGetAccountAsync(ConnectionStringNames.Storage, cancellationToken);
            ValidateStorageAccount(account, ConnectionStringNames.Storage);
            return account;
        }

        public static async Task<IStorageAccount> GetStorageAccountAsync(this IStorageAccountProvider provider, string connectionStringName, CancellationToken cancellationToken, INameResolver nameResolver = null)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            connectionStringName = string.IsNullOrEmpty(connectionStringName) ? ConnectionStringNames.Storage : connectionStringName;

            if (nameResolver != null)
            {
                string resolved = null;
                if (nameResolver.TryResolveWholeString(connectionStringName, out resolved))
                {
                    connectionStringName = resolved;
                }
            }

            IStorageAccount account = await provider.TryGetAccountAsync(connectionStringName, cancellationToken);
            ValidateStorageAccount(account, connectionStringName);
            return account;
        }

        public static async Task<IStorageAccount> GetStorageAccountAsync(this IStorageAccountProvider provider, IConnectionProvider connectionProvider, CancellationToken cancellationToken, INameResolver nameResolver = null)
        {
            return await provider.GetStorageAccountAsync(connectionProvider.Connection, cancellationToken, nameResolver);
        }

        private static void ValidateStorageAccount(IStorageAccount account, string connectionStringName)
        {
            if (account == null)
            {
                string message = StorageAccountParser.FormatParseAccountErrorMessage(StorageAccountParseResult.MissingOrEmptyConnectionStringError, connectionStringName);
                throw new InvalidOperationException(message);
            }
        }
    }
}
