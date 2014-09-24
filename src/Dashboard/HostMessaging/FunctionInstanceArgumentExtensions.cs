// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Dashboard.Data;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.HostMessaging
{
    internal static class FunctionInstanceArgumentExtensions
    {
        public static CloudStorageAccount GetStorageAccount(this FunctionInstanceArgument functionInstanceArgument)
        {
            if (functionInstanceArgument == null)
            {
                throw new ArgumentNullException("functionInstanceArgument");
            }

            string storageAccountName = functionInstanceArgument.AccountName;

            if (storageAccountName != null)
            {
                // Try to find specific connection string
                CloudStorageAccount specificAccount = AccountProvider.GetAccount(storageAccountName);

                if (specificAccount != null)
                {
                    return specificAccount;
                }
            }

            // If not found, try the dashboard connection string
            CloudStorageAccount dashboardAccount = AccountProvider.GetAccount(ConnectionStringNames.Dashboard);

            if (AccountNameMatches(storageAccountName, dashboardAccount))
            {
                return dashboardAccount;
            }

            // If still not found, try the default storage connection string
            CloudStorageAccount storageAccount = AccountProvider.GetAccount(ConnectionStringNames.Storage);

            if (AccountNameMatches(storageAccountName, storageAccount))
            {
                return storageAccount;
            }

            return null;
        }

        private static bool AccountNameMatches(string accountName, CloudStorageAccount account)
        {
            if (account == null || account.Credentials == null)
            {
                return false;
            }

            return String.Equals(accountName, account.Credentials.AccountName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
