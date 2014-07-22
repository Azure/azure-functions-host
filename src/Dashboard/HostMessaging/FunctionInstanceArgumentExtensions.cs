// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage;
using Dashboard.Data;

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
                // If not found, try default storage connection string
                string defaultConnectionString = provider.GetConnectionString(ConnectionStringNames.Storage);
                if (!String.IsNullOrEmpty(defaultConnectionString))
                {
                    var storageAccount = CloudStorageAccount.Parse(defaultConnectionString);

                    if (storageAccount.Credentials.AccountName.Equals(storageAccountName))
                    {
                        connectionString = defaultConnectionString;
                    }
                }
            }

            return connectionString;
        }
    }
}
