// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
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

            connectionStringName = connectionStringName ?? ConnectionStringNames.Storage;

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

        /// <summary>
        /// Determine whether the specified parameter declares an explicit storage
        /// account to use, either by specifying a value for <see cref="IConnectionProvider.Connection"/> or
        /// if <see cref="StorageAccountAttribute"/> has been applied up the hierarchy.
        /// </summary>
        internal static string GetAccountOverrideOrNull(ParameterInfo parameter)
        {
            // if this is a Storage attribute (e.g. Queues/Blobs/Tables) and
            // it specifies an account, return it
            var storageAttribute = parameter.GetCustomAttribute<StorageAccountAttribute>();
            if (storageAttribute != null && !string.IsNullOrEmpty(storageAttribute.Account))
            {
                return storageAttribute.Account;
            }

            // walk up from the parameter looking for any StorageAccountAttribute overrides
            var storageAccountAttribute = TypeUtility.GetHierarchicalAttributeOrNull<StorageAccountAttribute>(parameter);
            if (storageAccountAttribute != null)
            {
                return storageAccountAttribute.Account;
            }

            return null;
        }
    }
}
