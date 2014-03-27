using System;
using System.Reflection;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static partial class Utility
    {
        public static BindResult<T> StrongWrapper<T>(BindResult b)
        {
            return new BindResult<T>((T)b.Result, b);
        }

        // C# "ref" keyword
        public static bool IsRefKeyword(ParameterInfo p)
        {
            return (p.ParameterType.IsByRef && !p.IsOut);
        }

        public static CloudStorageAccount GetAccount(string AccountConnectionString)
        {
            CloudStorageAccount account;
            if (!CloudStorageAccount.TryParse(AccountConnectionString, out account))
            {
                throw new InvalidOperationException("account connection string is invalid");
            }
            return account;
        }

        public static string GetAccountName(string accountConnectionString)
        {
            CloudStorageAccount account = GetAccount(accountConnectionString);
            return GetAccountName(account);
        }

        public static string GetAccountName(CloudStorageAccount account)
        {
            return account.Credentials.AccountName;
        }
    }
}
