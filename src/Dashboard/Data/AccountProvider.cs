// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.Data
{
    public static class AccountProvider
    {
        [CLSCompliant(false)]
        public static CloudStorageAccount GetAccount(string connectionStringName)
        {
            string connectionString = ConnectionStringProvider.GetConnectionString(connectionStringName);

            if (connectionString == null)
            {
                return null;
            }

            CloudStorageAccount account;

            if (!CloudStorageAccount.TryParse(connectionString, out account))
            {
                return null;
            }

            return account;
        }

        [CLSCompliant(false)]
        public static IReadOnlyDictionary<string, CloudStorageAccount> GetAccounts()
        {
            Dictionary<string, CloudStorageAccount> accounts = new Dictionary<string, CloudStorageAccount>();

            IReadOnlyDictionary<string, string> connectionStrings = ConnectionStringProvider.GetConnectionStrings();

            foreach (KeyValuePair<string, string> item in connectionStrings)
            {
                CloudStorageAccount account;

                if (CloudStorageAccount.TryParse(item.Value, out account))
                {
                    accounts.Add(item.Key, account);
                }
            }

            return accounts;
        }
    }
}
