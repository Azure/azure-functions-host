using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class StringToCloudBlobConverter : IConverter<string, ICloudBlob>
    {
        private readonly CloudBlobClient _client;
        private readonly string _defaultContainerName;
        private readonly string _defaultBlobName;
        private readonly Type _argumentType;

        public StringToCloudBlobConverter(CloudBlobClient client, string defaultContainerName, string defaultBlobName,
            Type argumentType)
        {
            _client = client;
            _defaultContainerName = defaultContainerName;
            _defaultBlobName = defaultBlobName;
            _argumentType = argumentType;
        }

        public ICloudBlob Convert(string input)
        {
            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input))
            {
                CloudBlobContainer defaultContainer = _client.GetContainerReference(_defaultContainerName);

                if (_argumentType == typeof(CloudBlockBlob))
                {
                    return defaultContainer.GetBlockBlobReference(_defaultBlobName);
                }
                else if (_argumentType == typeof(CloudPageBlob))
                {
                    return defaultContainer.GetPageBlobReference(_defaultBlobName);
                }
                else
                {
                    return defaultContainer.GetExistingOrNewBlockBlobReference(_defaultBlobName);
                }
            }

            CloudBlobPath path = new CloudBlobPath(input);
            BlobClient.ValidateContainerName(path.ContainerName);
            BlobClient.ValidateBlobName(path.BlobName);
            CloudBlobContainer container = _client.GetContainerReference(path.ContainerName);
            return container.GetBlobReferenceFromServer(path.BlobName);
        }
    }
}
