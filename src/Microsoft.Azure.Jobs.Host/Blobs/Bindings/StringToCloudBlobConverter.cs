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
                defaultContainer.CreateIfNotExists();
                return defaultContainer.GetBlobReferenceForArgumentType(_defaultBlobName, _argumentType);
            }

            CloudBlobPath path = new CloudBlobPath(input);
            BlobClient.ValidateContainerName(path.ContainerName);
            BlobClient.ValidateBlobName(path.BlobName);
            CloudBlobContainer container = _client.GetContainerReference(path.ContainerName);
            container.CreateIfNotExists();
            return container.GetBlobReferenceForArgumentType(path.BlobName, _argumentType);
        }
    }
}
