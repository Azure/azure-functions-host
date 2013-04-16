using System;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // Stuff that's shared between RunnerHost and Executor?

    // Full permission to a queue
    public class CloudQueueDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string QueueName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }

        public CloudQueue GetQueue()
        {
            var q = GetAccount().CreateCloudQueueClient().GetQueueReference(QueueName);
            q.CreateIfNotExist();
            return q;
        }

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            return string.Format(@"{0}\{1}", accountName, QueueName);
        }

        public override string ToString()
        {
            return GetId();
        }
    }

    // Full permission to a Table
    // This can be serialized.
    public class CloudTableDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string TableName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }
    }

    // Full permission to a blob
    // $$$ This class morphed a litte. Is this now the same as CloudBlobContainer?
    // - vs. CloudBlob: this blobName can be null, this can refer to open. things.
    // - vs. CloudBlobPath: this has account info.
    public class CloudBlobDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string ContainerName { get; set; }

        public string BlobName { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }

        public CloudBlobContainer GetContainer()
        {
            var client = GetAccount().CreateCloudBlobClient();
            var c = Utility.GetContainer(client, ContainerName);
            return c;
        }

        public CloudBlob GetBlob()
        {
            var c = GetContainer();
            c.CreateIfNotExist();
            var blob = c.GetBlobReference(BlobName);
            return blob;
        }

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            return string.Format(@"{0}\{1}\{2}", accountName, ContainerName, BlobName);
        }

        public override string ToString()
        {
            return GetId();
        }

        // Get an absolute URL that's a Shared Access Signature for the given blob.
        public string GetContainerSasSig(SharedAccessPermissions permissions = SharedAccessPermissions.Read | SharedAccessPermissions.Write)
        {
            CloudBlobContainer container = this.GetContainer();

            string sasQueryString = container.GetSharedAccessSignature(
                new SharedAccessPolicy
                {
                    Permissions = permissions,
                    SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(45)
                });

            var uri = container.Uri.ToString() + sasQueryString;
            return uri;
        }

        public string GetBlobSasSig(SharedAccessPermissions permissions = SharedAccessPermissions.Read | SharedAccessPermissions.Write)
        {
            var blob = this.GetBlob();

            string sasQueryString = blob.GetSharedAccessSignature(
                new SharedAccessPolicy
                {
                    Permissions = permissions,
                    SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(45)
                });

            var uri = blob.Uri.ToString() + sasQueryString;
            return uri;
        }

        public override int GetHashCode()
        {
            return GetId().GetHashCode();
        }

        public override bool Equals(object obj)
        {
            CloudBlobDescriptor descriptor = obj as CloudBlobDescriptor;
            return descriptor != null && descriptor.GetId() == GetId();
        }
    }
}