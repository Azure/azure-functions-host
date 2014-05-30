using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class StringToCloudBlobConverter : IConverter<string, ICloudBlob>
    {
        private readonly CloudBlobClient _client;

        public StringToCloudBlobConverter(CloudBlobClient client)
        {
            _client = client;
        }

        public ICloudBlob Convert(string input)
        {
            CloudBlobPath path = new CloudBlobPath(input);
            CloudBlobContainer container = _client.GetContainerReference(path.ContainerName);
            return container.GetBlobReferenceFromServer(path.BlobName);
        }
    }
}
