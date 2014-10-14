// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultStorageAccountProvider : IStorageAccountProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider =
            AmbientConnectionStringProvider.Instance;

        private readonly IStorageCredentialsValidator _storageCredentialsValidator =
            new DefaultStorageCredentialsValidator();

        private IStorageAccount _dashboardAccount;
        private bool _dashboardAccountSet;
        private IStorageAccount _storageAccount;
        private bool _storageAccountSet;

        public DefaultStorageAccountProvider()
        {
        }

        /// <summary>
        /// Initializes a new instance of the class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="dashboardAndStorageConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public DefaultStorageAccountProvider(string dashboardAndStorageConnectionString)
        {
            IStorageAccount account = ParseStorageAccount(dashboardAndStorageConnectionString);
            DashboardAccount = account;
            StorageAccount = account;
        }

        /// <summary>Gets or sets the Azure Storage connection string used for logging and diagnostics.</summary>
        public string DashboardConnectionString
        {
            get
            {
                if (!_dashboardAccountSet)
                {
                    return _ambientConnectionStringProvider.GetConnectionString(ConnectionStringNames.Dashboard);
                }

                // Intentionally access the field rather than the property to avoid setting _dashboardAccountSet.
                return _dashboardAccount != null ? _dashboardAccount.ToString(exportSecrets: true) : null;
            }
            set
            {
                DashboardAccount = ParseDashboardAccount(value, explicitlySet: true);
            }
        }

        /// <summary>Gets or sets the Azure Storage connection string used for reading and writing data.</summary>
        public string StorageConnectionString
        {
            get
            {
                if (!_storageAccountSet)
                {
                    return _ambientConnectionStringProvider.GetConnectionString(ConnectionStringNames.Storage);
                }

                // Intentionally access the field rather than the property to avoid setting _storageAccountSet.
                return _storageAccount != null ? _storageAccount.ToString(exportSecrets: true) : null;
            }
            set
            {
                StorageAccount = ParseStorageAccount(value);
            }
        }

        private IStorageAccount DashboardAccount
        {
            get
            {
                if (!_dashboardAccountSet)
                {
                    _dashboardAccount = ParseDashboardAccount(_ambientConnectionStringProvider.GetConnectionString(
                        ConnectionStringNames.Dashboard), explicitlySet: false);
                    _dashboardAccountSet = true;
                }

                return _dashboardAccount;
            }
            set
            {
                _dashboardAccount = value;
                _dashboardAccountSet = true;
            }
        }

        private IStorageAccount StorageAccount
        {
            get
            {
                if (!_storageAccountSet)
                {
                    _storageAccount = ParseStorageAccount(
                        _ambientConnectionStringProvider.GetConnectionString(ConnectionStringNames.Storage));
                    _storageAccountSet = true;
                }

                return _storageAccount;
            }
            set
            {
                _storageAccount = value;
                _storageAccountSet = true;
            }
        }

        public async Task<IStorageAccount> GetAccountAsync(string connectionStringName,
            CancellationToken cancellationToken)
        {
            IStorageAccount account;

            if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                account = DashboardAccount;
            }
            else if (connectionStringName == ConnectionStringNames.Storage)
            {
                account = StorageAccount;
            }
            else
            {
                account =null;
            }

            if (account != null)
            {
                // On the first attempt, this will make a network call to verify the credentials work.
                await _storageCredentialsValidator.ValidateCredentialsAsync(account, cancellationToken);
            }

            return account;
        }

        private IStorageAccount ParseDashboardAccount(string connectionString, bool explicitlySet)
        {
            if (explicitlySet && String.IsNullOrEmpty(connectionString))
            {
                return null;
            }

            return ParseAccount(connectionString, ConnectionStringNames.Dashboard);
        }

        private IStorageAccount ParseStorageAccount(string connectionString)
        {
            return ParseAccount(connectionString, ConnectionStringNames.Storage);
        }

        private static IStorageAccount ParseAccount(string connectionString, string connectionStringName)
        {
            CloudStorageAccount sdkAccount = StorageAccountParser.ParseAccount(connectionString, connectionStringName);
            return new StorageAccount(sdkAccount);
        }
    }
}
