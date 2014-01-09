using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
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
            if (accountConnectionString == null)
            {
                throw new InvalidOperationException("Windows Azure Storage account connection string is missing.");
            }

            if (accountConnectionString == string.Empty)
            {
                throw new InvalidOperationException("Windows Azure Storage account connection string value is missing.");
            }

            // Will throw on parser errors. 
            CloudStorageAccount account;
            if (!CloudStorageAccount.TryParse(accountConnectionString, out account))
            {
                throw new InvalidOperationException("Windows Azure Storage account connection string is not formatted correctly. Please visit http://msdn.microsoft.com/en-us/library/windowsazure/ee758697.aspx for details about configuring Windows Azure Storage connection strings.");
            }

            if (IsDevelopmentStorageAccount(account))
            {
                throw new InvalidOperationException("The Windows Azure Storage Emulator is not supported, please use a Windows Azure Storage account hosted in Windows Azure.");
            }

            // Verify the credentials are correct.
            // Have to actually ping a storage operation. 
            try
            {
                var client = account.CreateCloudBlobClient();

                // This can hang for a long time if the account name is wrong. 
                // If will fail fast if the password is incorrect.
                client.GetServiceProperties();
            }
            catch
            {
                string msg = string.Format("The account credentials for '{0}' are incorrect.", account.Credentials.AccountName);
                throw new InvalidOperationException(msg);
            }
        }

        private static bool IsDevelopmentStorageAccount(CloudStorageAccount account)
        {
            // see the section "Addressing local storage resources" in http://msdn.microsoft.com/en-us/library/windowsazure/hh403989.aspx 
            return account.BlobEndpoint.PathAndQuery.TrimStart('/') == account.Credentials.AccountName;
        }
    }
}
