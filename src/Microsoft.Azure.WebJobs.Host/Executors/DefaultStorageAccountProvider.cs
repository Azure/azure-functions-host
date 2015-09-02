// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultStorageAccountProvider : IStorageAccountProvider
    {
        private readonly IConnectionStringProvider _ambientConnectionStringProvider;
        private readonly IStorageCredentialsValidator _storageCredentialsValidator;
        private readonly IStorageAccountParser _storageAccountParser;
        private readonly IServiceProvider _services;

        private IStorageAccount _dashboardAccount;
        private bool _dashboardAccountSet;
        private IStorageAccount _storageAccount;
        private bool _storageAccountSet;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="services">The <see cref="IServiceProvider"/> to use.</param>
        public DefaultStorageAccountProvider(IServiceProvider services)
            : this(services, AmbientConnectionStringProvider.Instance, new StorageAccountParser(), new DefaultStorageCredentialsValidator())
        {
        }

        /// <summary>
        /// Initializes a new instance of the class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="services">The <see cref="IServiceProvider"/> to use.</param>
        /// <param name="dashboardAndStorageConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public DefaultStorageAccountProvider(IServiceProvider services, string dashboardAndStorageConnectionString)
            : this(services)
        {
            StorageConnectionString = dashboardAndStorageConnectionString;
            DashboardAccount = StorageAccount;
        }

        internal DefaultStorageAccountProvider(IServiceProvider services, IConnectionStringProvider ambientConnectionStringProvider, 
            IStorageAccountParser storageAccountParser, IStorageCredentialsValidator storageCredentialsValidator)
        {
            if (services == null)
            {
                throw new ArgumentNullException("services");
            }

            if (ambientConnectionStringProvider == null)
            {
                throw new ArgumentNullException("ambientConnectionStringProvider");
            }

            if (storageAccountParser == null)
            {
                throw new ArgumentNullException("storageAccountParser");
            }

            if (storageCredentialsValidator == null)
            {
                throw new ArgumentNullException("storageCredentialsValidator");
            }

            _services = services;
            _ambientConnectionStringProvider = ambientConnectionStringProvider;
            _storageCredentialsValidator = storageCredentialsValidator;
            _storageAccountParser = storageAccountParser;
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
                DashboardAccount = !String.IsNullOrEmpty(value) ? ParseAccount(ConnectionStringNames.Dashboard, value) : null;
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
                StorageAccount = !String.IsNullOrEmpty(value) ? ParseAccount(ConnectionStringNames.Storage, value) : null;
            }
        }

        private IStorageAccount DashboardAccount
        {
            get
            {
                if (!_dashboardAccountSet)
                {
                    _dashboardAccount = ParseAccount(ConnectionStringNames.Dashboard);
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
                    _storageAccount = ParseAccount(ConnectionStringNames.Storage);
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

        public async Task<IStorageAccount> GetAccountAsync(string connectionStringName, CancellationToken cancellationToken)
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
                account = null;
            }

            // Only dashboard may be null when requested.
            if (account == null && connectionStringName != ConnectionStringNames.Dashboard)
            {
                throw new InvalidOperationException(StorageAccountParser.FormatParseAccountErrorMessage(
                    StorageAccountParseResult.MissingOrEmptyConnectionStringError, connectionStringName));
            }

            if (account != null)
            {
                // On the first attempt, this will make a network call to verify the credentials work.
                await _storageCredentialsValidator.ValidateCredentialsAsync(account, cancellationToken);
            }

            return account;
        }

        private IStorageAccount ParseAccount(string connectionStringName)
        {
            string connectionString = _ambientConnectionStringProvider.GetConnectionString(connectionStringName);
            return ParseAccount(connectionStringName, connectionString);
        }

        private IStorageAccount ParseAccount(string connectionStringName, string connectionString)
        {
            return _storageAccountParser.ParseAccount(connectionString, connectionStringName, _services);
        }
    }
}
