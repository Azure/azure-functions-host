using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using SimpleBatch;

namespace RunnerInterfaces
{
    public static class DictExtensions
    {
        public static TValue GetOrCreate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new()
        {
            TValue value;
            if (!dict.TryGetValue(key, out value))
            {
                value = new TValue();
                dict[key] = value;
            }
            return value;
        }
    }

    public static partial class Utility
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
    }
}
