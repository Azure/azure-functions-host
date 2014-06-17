using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
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

        public CloudBlockBlob GetBlockBlob()
        {
            if (String.IsNullOrEmpty(blobName))
            {
                throw new InvalidOperationException("The blob name must not be null or empty.");
            }

            var c = GetContainer();
            c.CreateIfNotExists();
            var blob = c.GetBlockBlobReference(BlobName);
            return blob;
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
