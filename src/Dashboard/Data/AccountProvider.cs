// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;

namespace Dashboard.Data
{
    public static class AccountProvider
    {
        private static Dictionary<string, CloudStorageAccount> _accounts;
        
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
        
        public static CloudStorageAccount GetAccountByName(string accountName)
        {
            var accountMap = AccountProvider.GetAccounts();
            foreach (var currAccount in accountMap.Values)
            {
                if (AccountNameMatches(accountName, currAccount))
                {
                    return currAccount;
                }
            }

            return null;
        }
        
        public static IReadOnlyDictionary<string, CloudStorageAccount> GetAccounts()
        {
            if (_accounts == null)
            {
                Dictionary<string, CloudStorageAccount> accounts = new Dictionary<string, CloudStorageAccount>();
                IReadOnlyDictionary<string, string> connectionStrings = ConnectionStringProvider.GetPossibleConnectionStrings();

                foreach (KeyValuePair<string, string> item in connectionStrings)
                {
                    CloudStorageAccount account;
                    if (CloudStorageAccount.TryParse(item.Value, out account))
                    {
                        accounts.Add(item.Key, account);
                    }
                }

                _accounts = accounts;
            }

            return _accounts;
        }
        
        public static bool AccountNameMatches(string accountName, CloudStorageAccount account)
        {
            if (account == null || account.Credentials == null)
            {
                return false;
            }

            return String.Equals(accountName, account.Credentials.AccountName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
