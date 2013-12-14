using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{    
    internal static partial class Utility
    {
        public static void WriteBlob(CloudStorageAccount account, string containerName, string blobName, string contents)
        {
            var client = account.CreateCloudBlobClient();
            var c = GetContainer(client, containerName);
            c.CreateIfNotExist();

            var b = c.GetBlobReference(blobName);

            b.UploadText(contents);
        }

        [DebuggerNonUserCode]
        public static void DeleteContainer(CloudStorageAccount account, string containerName)
        {
            var client = account.CreateCloudBlobClient();
            var c = GetContainer(client, containerName);
            try
            {
                c.Delete();
            }
            catch (StorageClientException)
            {
            }
        }

        public static DateTime? GetBlobModifiedUtcTime(CloudBlob blob)
        {
            if (!DoesBlobExist(blob))
            {
                return null; // no blob, no time.
            }

            var props = blob.Properties;
            var time = props.LastModifiedUtc;
            return time;
        }

        public static void DeleteBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            var client = account.CreateCloudBlobClient();
            var c = GetContainer(client, containerName);
            CloudBlob blob = c.GetBlobReference(blobName);
            blob.DeleteIfExists();
        }

        [DebuggerNonUserCode]
        public static bool DoesBlobExist(CloudBlob blob)
        {
            try
            {
                blob.FetchAttributes(); // force network call to test whether it exists
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool DoesBlobExist(CloudStorageAccount account, string containerName, string blobName)
        {
            var client = account.CreateCloudBlobClient();
            var c = GetContainer(client, containerName);
            var blob = c.GetBlobReference(blobName);

            return DoesBlobExist(blob);
        }

        // Return Null if doesn't exist
        [DebuggerNonUserCode]
        public static string ReadBlob(CloudBlob blob)
        {
            // Beware! Blob.DownloadText does not strip the BOM! 
            try
            {                
                using (var stream = blob.OpenRead())
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks : true))
                {
                    string data = sr.ReadToEnd();
                    return data;
                }               
            }
            catch
            {
                return null;
            }
        }

        // Return Null if doesn't exist
        public static string ReadBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            var client = account.CreateCloudBlobClient();
            var c = GetContainer(client, containerName);
            var blob = c.GetBlobReference(blobName);
            return ReadBlob(blob);
        }

        public static CloudBlob GetBlob(string accountConnectionString, string containerName, string blobName)
        {
            var account = Utility.GetAccount(accountConnectionString);
            return GetBlob(account, containerName, blobName);
        }

        public static CloudBlob GetBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            var client = account.CreateCloudBlobClient();
            var c = GetContainer(client, containerName);
            var blob = c.GetBlobReference(blobName);
            return blob;
        }

        // GEt container, and validate the name.

        public static CloudBlobContainer GetContainer(string accountConnectionString, string containerName)
        {
            CloudBlobClient client = Utility.GetAccount(accountConnectionString).CreateCloudBlobClient();
            return GetContainer(client, containerName);
        }
    
        public static CloudBlobContainer GetContainer(CloudBlobClient client, string containerName)
        {
            ValidateContainerName(containerName);
            return client.GetContainerReference(containerName);
        }

        // Naming rules are here: http://msdn.microsoft.com/en-us/library/dd135715.aspx
        // Validate this on the client side so that we can get a user-friendly error rather than a 400.
        // See code here: http://social.msdn.microsoft.com/Forums/en-GB/windowsazuredata/thread/d364761b-6d9d-4c15-8353-46c6719a3392
        public static void ValidateContainerName(string containerName)
        {
            if (containerName == null)
            {
                throw new ArgumentNullException("containerName");
            }
            if (containerName.Equals("$root"))
            {
                return; 
            }

            if (!Regex.IsMatch(containerName, @"^[a-z0-9](([a-z0-9\-[^\-])){1,61}[a-z0-9]$"))
            {
                throw new Exception("Invalid container name: " + containerName);
            }
        }
    }
}