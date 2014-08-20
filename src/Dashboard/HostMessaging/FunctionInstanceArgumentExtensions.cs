// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Dashboard.Data;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.HostMessaging
{
    internal static class FunctionInstanceArgumentExtensions
    {
        public static string GetStorageConnectionString(this FunctionInstanceArgument functionInstanceArgument)
        {
            if (functionInstanceArgument == null)
            {
                throw new ArgumentNullException("functionInstanceArgument");
            }

            DefaultStorageAccountProvider provider = new DefaultStorageAccountProvider();

            string connectionString = null;

            string storageAccountName = functionInstanceArgument.AccountName;
            if (storageAccountName != null)
            {
                // Try to find specific connection string
                connectionString = provider.GetConnectionString(storageAccountName);
            }

            if (String.IsNullOrEmpty(connectionString))
            {
                // If not found, try the dashboard connection string
                var account = GetAccount(provider, ConnectionStringNames.Dashboard);

                if (account != null && String.Equals(storageAccountName, account.Credentials.AccountName))
                {
                    connectionString = account.ToString(exportSecrets: true);
                }
            }

            if (String.IsNullOrEmpty(connectionString))
            {
                // If still not found, try the default storage connection string
                var account = GetAccount(provider, ConnectionStringNames.Storage);

                if (account != null && String.Equals(storageAccountName, account.Credentials.AccountName))
                {
                    connectionString = account.ToString(exportSecrets: true);
                }
            }

            return connectionString;
        }

        private static CloudStorageAccount GetAccount(IConnectionStringProvider provider, string connectionStringName)
        {
            string connectionString = provider.GetConnectionString(ConnectionStringNames.Dashboard);

            if (String.IsNullOrEmpty(connectionString))
            {
                return null;
            }
            else
            {
                return CloudStorageAccount.Parse(connectionString);
            }
        }
    }
}
