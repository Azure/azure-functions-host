using System;
using System.Globalization;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Full permission to a blob
    // $$$ This class morphed a litte. Is this now the same as CloudBlobContainer?
    // - vs. CloudBlob: this blobName can be null, this can refer to open. things.
    // - vs. CloudBlobPath: this has account info.
    internal class CloudBlobDescriptor
    {
        public string AccountConnectionString { get; set; }

        public string ContainerName
        {
            get
            {
                return containerName;
            }
            set
            {
                BlobClient.ValidateContainerName(value);
                containerName = value;
            }
        }

        private string blobName;
        private string containerName;

        public string BlobName
        {
            get
            {
                return blobName;
            }
            set
            {
                if (blobName != null)
                {
                    BlobClient.ValidateBlobName(blobName);
                }
                blobName = value;
            }
        }

        private CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(AccountConnectionString);
        }

        private CloudBlobClient CreateClient()
        {
            return GetAccount().CreateCloudBlobClient();
        }

        private CloudBlobContainer GetContainer()
        {
            var client = CreateClient();
            var c = BlobClient.GetContainer(client, ContainerName);
            return c;
        }

        public CloudBlob GetBlob()
        {
            if (String.IsNullOrEmpty(blobName))
            {
                throw new InvalidOperationException("The blob name must not be null or empty.");
            }

            var c = GetContainer();
            c.CreateIfNotExist();
            var blob = c.GetBlobReference(BlobName);
            return blob;
        }

        public CloudBlob TryGetBlob()
        {
            if (String.IsNullOrEmpty(containerName) || String.IsNullOrEmpty(blobName))
            {
                return null;
            }

            CloudBlobClient client = CreateClient();
            CloudBlobContainer container = client.GetContainerReference(containerName);
            return container.GetBlobReference(blobName);
        }

        public string GetId()
        {
            string accountName = GetAccount().Credentials.AccountName;
            // TODO: shuold we generate an Id that is a valid blob path? (i.e. using '/' instead of '\' ?)
            return String.Format(CultureInfo.InvariantCulture, @"{0}\{1}\{2}", accountName, ContainerName, BlobName);
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
