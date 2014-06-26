using System.Diagnostics;
using System.IO;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    internal static class TestBlobClient
    {
        public static void WriteBlob(CloudStorageAccount account, string containerName, string blobName, string contents)
        {
            var client = account.CreateCloudBlobClient();
            var c = client.GetContainerReference(containerName);
            c.CreateIfNotExists();

            var b = c.GetBlockBlobReference(blobName);

            b.UploadText(contents);
        }

        [DebuggerNonUserCode]
        public static void DeleteContainer(CloudStorageAccount account, string containerName)
        {
            var client = account.CreateCloudBlobClient();
            var c = client.GetContainerReference(containerName);
            try
            {
                c.Delete();
            }
            catch (StorageException)
            {
            }
        }

        // Return Null if doesn't exist
        public static string ReadBlob(CloudStorageAccount account, string containerName, string blobName)
        {
            var client = account.CreateCloudBlobClient();
            var c = client.GetContainerReference(containerName);
            ICloudBlob blob;
            try
            {
                blob = c.GetBlobReferenceFromServer(blobName);
            }
            catch (StorageException)
            {
                return null;
            }
            return ReadBlob(blob);
        }

        public static bool DoesBlobExist(CloudStorageAccount account, string containerName, string blobName)
        {
            var client = account.CreateCloudBlobClient();
            var c = client.GetContainerReference(containerName);
            var blob = c.GetBlockBlobReference(blobName);

            return blob.Exists();
        }

        // Return Null if doesn't exist
        [DebuggerNonUserCode]
        private static string ReadBlob(ICloudBlob blob)
        {
            // Beware! Blob.DownloadText does not strip the BOM! 
            try
            {
                using (var stream = blob.OpenRead())
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
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
    }
}
