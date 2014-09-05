// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultStorageAccountProvider : IStorageAccountProvider, IConnectionStringProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider = new AmbientConnectionStringProvider();

        private CloudStorageAccount _dashboardAccount;
        private bool _dashboardAccountSet;
        private CloudStorageAccount _storageAccount;
        private bool _storageAccountSet;
        private string _serviceBusConnectionString;
        private bool _serviceBusConnectionStringSet;

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
            CloudStorageAccount account = ParseStorageAccount(dashboardAndStorageConnectionString);
            _dashboardAccount = account;
            _dashboardAccountSet = true;
            _storageAccount = account;
            _storageAccountSet = true;
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

                return _dashboardAccount != null ? _dashboardAccount.ToString(exportSecrets: true) : null;
            }
            set
            {
                _dashboardAccount = ParseDashboardAccount(value, explicitlySet: true);
                _dashboardAccountSet = true;
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

                return _storageAccount != null ? _storageAccount.ToString(exportSecrets: true) : null;
            }
            set
            {
                _storageAccount = ParseStorageAccount(value);
                _storageAccountSet = true;
            }
        }

        /// <summary>Gets or sets the Azure Service bus connection string.</summary>
        public string ServiceBusConnectionString
        {
            get
            {
                if (!_serviceBusConnectionStringSet)
                {
                    return _ambientConnectionStringProvider.GetConnectionString(ConnectionStringNames.ServiceBus);
                }

                return _serviceBusConnectionString;
            }
            set
            {
                _serviceBusConnectionString = value;
                _serviceBusConnectionStringSet = true;
            }
        }

        public CloudStorageAccount GetAccount(string connectionStringName)
        {
            if (connectionStringName == ConnectionStringNames.Dashboard)
            {
                if (!_dashboardAccountSet)
                {
                    _dashboardAccount = ParseDashboardAccount(
                        _ambientConnectionStringProvider.GetConnectionString(connectionStringName),
                        explicitlySet: false);
                    _dashboardAccountSet = true;
                }

                return _dashboardAccount;
            }
            else if (connectionStringName == ConnectionStringNames.Storage)
            {
                if (!_storageAccountSet)
                {
                    _storageAccount = ParseStorageAccount(
                        _ambientConnectionStringProvider.GetConnectionString(connectionStringName));
                    _storageAccountSet = true;
                }

                return _storageAccount;
            }
            else
            {
                return null;
            }
        }

        public string GetConnectionString(string connectionStringName)
        {
            if (connectionStringName == ConnectionStringNames.Dashboard ||
                connectionStringName == ConnectionStringNames.Storage)
            {
                return GetAccount(connectionStringName).ToString(exportSecrets: true);
            }
            else if (connectionStringName == ConnectionStringNames.ServiceBus)
            {
                if (!_serviceBusConnectionStringSet)
                {
                    _serviceBusConnectionString =
                        _ambientConnectionStringProvider.GetConnectionString(connectionStringName);
                    _serviceBusConnectionStringSet = true;
                }

                return _serviceBusConnectionString;
            }
            else
            {
                return _ambientConnectionStringProvider.GetConnectionString(connectionStringName);
            }
        }

        public IReadOnlyDictionary<string, string> GetConnectionStrings()
        {
            return _ambientConnectionStringProvider.GetConnectionStrings();
        }

        private CloudStorageAccount ParseDashboardAccount(string connectionString, bool explicitlySet)
        {
            if (explicitlySet && String.IsNullOrEmpty(connectionString))
            {
                return null;
            }

            return StorageAccountParser.ParseAccount(connectionString, ConnectionStringNames.Dashboard);
        }

        private CloudStorageAccount ParseStorageAccount(string connectionString)
        {
            return StorageAccountParser.ParseAccount(connectionString, ConnectionStringNames.Storage);
        }
    }
}
