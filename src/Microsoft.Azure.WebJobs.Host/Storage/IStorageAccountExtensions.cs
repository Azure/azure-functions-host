// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host.Storage
{
    internal static class StorageAccountExtensions
    {
        public static void AssertTypeOneOf(this IStorageAccount account, params StorageAccountType[] types)
        {
            if (!types.Contains(account.Type))
            {
                var message = string.Format(CultureInfo.CurrentCulture,
                    "Storage account '{0}' is of unsupported type '{1}'. Supported types are '{2}'", 
                    account.Credentials.AccountName, account.Type.GetFriendlyDescription(), String.Join("', '", types.Select(type => type.GetFriendlyDescription())));
                throw new InvalidOperationException(message);
            }
        }
    }
}