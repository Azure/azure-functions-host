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
        // Move to utility
        public static void PostJson(string url, object body)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            WebRequest request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length; // set before writing to stream
            var stream = request.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Close();

            var response = request.GetResponse(); // does the actual web request
        }

        public static BindResult<T> StrongWrapper<T>(BindResult b)
        {
            return new BindResult<T>((T)b.Result, b);

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
            var a = GetAccount(accountConnectionString);
            return a.Credentials.AccountName;
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
