// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

            return provider.GetAccountAsync(ConnectionStringNames.Dashboard, cancellationToken);
        }

        public static Task<IStorageAccount> GetStorageAccountAsync(this IStorageAccountProvider provider, CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            return provider.GetAccountAsync(ConnectionStringNames.Storage, cancellationToken);
        }

        public static Task<IStorageAccount> GetStorageAccountAsync(this IStorageAccountProvider provider, ParameterInfo parameter, CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            string connectionStringName = GetAccountOverrideOrNull(parameter);
            if (string.IsNullOrEmpty(connectionStringName))
            {
                connectionStringName = ConnectionStringNames.Storage;
            }

            return provider.GetAccountAsync(connectionStringName, cancellationToken);
        }

        /// <summary>
        /// Walk from the parameter up to the containing type, looking for a
        /// <see cref="StorageAccountAttribute"/>. If found, return the account.
        /// </summary>
        internal static string GetAccountOverrideOrNull(ParameterInfo parameter)
        {
            if (parameter == null || 
                parameter.GetType() == typeof(AttributeBindingSource.FakeParameterInfo))
            {
                return null;
            }

            StorageAccountAttribute attribute = parameter.GetCustomAttribute<StorageAccountAttribute>();
            if (attribute != null)
            {
                return attribute.Account;
            }

            attribute = parameter.Member.GetCustomAttribute<StorageAccountAttribute>();
            if (attribute != null)
            {
                return attribute.Account;
            }

            attribute = parameter.Member.DeclaringType.GetCustomAttribute<StorageAccountAttribute>();
            if (attribute != null)
            {
                return attribute.Account;
            }

            return null;
        }
    }
}
