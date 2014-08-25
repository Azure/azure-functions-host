// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;
using System.Collections.Generic;

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

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        public DefaultStorageAccountProvider()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="dashboardAndStorageConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public DefaultStorageAccountProvider(string dashboardAndStorageConnectionString)
        {
            CloudStorageAccount account = ValidateStorageAccount(dashboardAndStorageConnectionString);
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
                _dashboardAccount = ValidateDashboardAccount(value, explicitlySet: true);
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
                _storageAccount = ValidateStorageAccount(value);
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
                    _dashboardAccount = ValidateDashboardAccount(
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
                    _storageAccount = ValidateStorageAccount(
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

        private CloudStorageAccount ValidateDashboardAccount(string connectionString, bool explicitlySet)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                if (!explicitlySet)
                {
                    var msg = FormatConnectionStringError("dashboard", ConnectionStringNames.Dashboard,
                        "Microsoft Azure Storage account connection string is missing or empty.");
                    throw new InvalidOperationException(msg);
                }
                else
                {
                    return null;
                }
            }

            return ValidateAccount(connectionString, "dashboard", ConnectionStringNames.Dashboard);
        }

        private CloudStorageAccount ValidateStorageAccount(string connectionString)
        {
            return ValidateAccount(connectionString, "storage", ConnectionStringNames.Storage);
        }

        private static CloudStorageAccount ValidateAccount(string connectionString, string type, string name)
        {
            CloudStorageAccount account;
            string coreMessage;

            if (!TryParseAndValidateAccount(connectionString, out account, out coreMessage))
            {
                string message = FormatConnectionStringError(type, name, coreMessage);
                throw new InvalidOperationException(message);
            }

            return account;
        }

        /// <summary>
        /// Validate a Microsoft Azure Storage connection string, by parsing it, and placing
        /// a call to Azure to assert the credentials validity as well.
        /// </summary>
        internal static bool TryParseAndValidateAccount(string connectionString, out CloudStorageAccount account,
            out string errorMessage)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                errorMessage = "Microsoft Azure Storage account connection string is missing or empty.";
                account = null;
                return false;
            }

            // Will throw on parser errors.
            CloudStorageAccount possibleAccount;
            if (!CloudStorageAccount.TryParse(connectionString, out possibleAccount))
            {
                errorMessage = "Microsoft Azure Storage account connection string is not formatted " +
                    "correctly. Please visit http://msdn.microsoft.com/en-us/library/windowsazure/ee758697.aspx for " +
                    "details about configuring Microsoft Azure Storage connection strings.";
                account = null;
                return false;
            }

            if (StorageClient.IsDevelopmentStorageAccount(possibleAccount))
            {
                errorMessage = "The Microsoft Azure Storage Emulator is not supported, please use a " +
                    "Microsoft Azure Storage account hosted in Microsoft Azure.";
                account = null;
                return false;
            }

            account = possibleAccount;
            errorMessage = null;
            return true;
        }

        internal static string FormatConnectionStringError(string type, string name, string coreMessage)
        {
            return String.Format(CultureInfo.CurrentCulture,
                "Failed to validate Microsoft Azure WebJobs SDK {0} connection string: {2}" + Environment.NewLine +
                "The Microsoft Azure WebJobs SDK connection string is specified by setting a connection string named " +
                "'{1}' in the connectionStrings section of the .config file, or with an environment variable named " +
                "'{1}', or through JobHostConfiguration.",
                type,
                AmbientConnectionStringProvider.GetPrefixedConnectionStringName(name),
                coreMessage);
        }
    }
}
