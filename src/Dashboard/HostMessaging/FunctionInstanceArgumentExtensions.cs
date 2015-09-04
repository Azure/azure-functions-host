// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
            CloudStorageAccount storageAccount = AccountProvider.GetAccount(ConnectionStringNames.Dashboard);
            if (AccountProvider.AccountNameMatches(storageAccountName, storageAccount))
            {
                return storageAccount;
            }

            // If still not found, try the default storage connection string
            storageAccount = AccountProvider.GetAccount(ConnectionStringNames.Storage);
            if (AccountProvider.AccountNameMatches(storageAccountName, storageAccount))
            {
                return storageAccount;
            }

            // If still not found, try a final search through all known accounts
            // matching on account name
            var accountMap = AccountProvider.GetAccounts();
            foreach (var currAccount in accountMap.Values)
            {
                if (AccountProvider.AccountNameMatches(storageAccountName, currAccount))
                {
                    return currAccount;
                }
            }

            return null;
        }
    }
}
