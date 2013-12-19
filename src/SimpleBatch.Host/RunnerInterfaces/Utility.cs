using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;

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

        public static string GetConnectionString(CloudStorageAccount account)
        {
            return account.ToString(exportSecrets: true);
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

        [DebuggerNonUserCode]
        public static void DeleteDirectory(string localPath)
        {
            if (localPath != null)
            {
                try
                {
                    Directory.Delete(localPath, recursive: true);
                }
                catch
                {
                    // File lock or missing dir. Ignore. 
                }
            }
        }

        // Throw if the account string is bad (parser error, wrong credentials). 
        public static void ValidateConnectionString(string accountConnectionString)
        {
            // Will throw on parser errors. 
            CloudStorageAccount account;
            if (!CloudStorageAccount.TryParse(accountConnectionString, out account))
            {
                throw new InvalidOperationException("Account connection string is an invalid format");
            }

            // Verify the credentials are correct.
            // Have to actually ping a storage operation. 
            try
            {
                var client = account.CreateCloudBlobClient();

                // This can hang for a long time if the account name is wrong. 
                // If will fail fast if the password is incorrect.
                client.GetServiceProperties();

                // Success
            }
            catch
            {
                string msg = string.Format("The account credentials for '{0}' are incorrect.", account.Credentials.AccountName);
                throw new InvalidOperationException(msg);
            }
        }
    }
}
