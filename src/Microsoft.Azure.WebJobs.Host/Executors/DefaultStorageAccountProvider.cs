// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultStorageAccountProvider : IStorageAccountProvider, IConnectionStringProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider =
            new AmbientConnectionStringProvider();

        private IStorageAccount _dashboardAccount;
        private bool _dashboardAccountSet;
        private IStorageAccount _storageAccount;
        private bool _storageAccountSet;
        private string _serviceBusConnectionString;
        private bool _serviceBusConnectionStringSet;
        private IHostInstanceLogger _hostInstanceLogger;
        private IFunctionInstanceLogger _functionInstanceLogger;

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

        public IHostInstanceLogger HostInstanceLogger
        {
            get
            {
                EnsureDashboardAccount();
                return _hostInstanceLogger;
            }
        }

        public IFunctionInstanceLogger FunctionInstanceLogger
        {
            get
            {
                EnsureDashboardAccount();
                return _functionInstanceLogger;
            }
        }

        private IStorageAccount DashboardAccount
        {
            get
            {
                EnsureDashboardAccount();
                return _dashboardAccount;
            }
            set
            {
                _dashboardAccount = value;

                if (value != null)
                {
                    // Create logging against a live Azure account.
                    CloudBlobClient dashboardBlobClient = value.SdkObject.CreateCloudBlobClient();
                    IPersistentQueueWriter<PersistentQueueMessage> queueWriter =
                        new PersistentQueueWriter<PersistentQueueMessage>(dashboardBlobClient);
                    PersistentQueueLogger queueLogger = new PersistentQueueLogger(queueWriter);
                    _hostInstanceLogger = queueLogger;
                    _functionInstanceLogger = new CompositeFunctionInstanceLogger(queueLogger,
                        new ConsoleFunctionInstanceLogger());
                }
                else
                {
                    // No auxillary logging. Logging interfaces are nops or in-memory.
                    _hostInstanceLogger = new NullHostInstanceLogger();
                    _functionInstanceLogger = new ConsoleFunctionInstanceLogger();
                }

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

        public IStorageAccount GetAccount(string connectionStringName)
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

        private void EnsureDashboardAccount()
        {
            if (!_dashboardAccountSet)
            {
                DashboardAccount = ParseDashboardAccount(_ambientConnectionStringProvider.GetConnectionString(
                    ConnectionStringNames.Dashboard), explicitlySet: false);
            }
        }
    }
}
