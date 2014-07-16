// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class StorageClient
    {
        public static string GetAccountName(CloudStorageAccount account)
        {
            if (account == null)
            {
                return null;
            }

            return GetAccountName(account.Credentials);
        }

        public static string GetAccountName(StorageCredentials credentials)
        {
            if (credentials == null)
            {
                return null;
            }

            return credentials.AccountName;
        }

        public static bool IsDevelopmentStorageAccount(CloudStorageAccount account)
        {
            // see the section "Addressing local storage resources" in http://msdn.microsoft.com/en-us/library/windowsazure/hh403989.aspx 
            return String.Equals(
                account.BlobEndpoint.PathAndQuery.TrimStart('/'),
                account.Credentials.AccountName,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
