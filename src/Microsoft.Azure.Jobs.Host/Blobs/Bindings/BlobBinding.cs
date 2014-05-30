using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class BlobBinding : IBinding
    {
        private readonly IArgumentBinding<ICloudBlob> _argumentBinding;
        private readonly CloudBlobClient _client;
        private readonly string _containerName;
        private readonly string _blobName;
        private readonly IObjectToTypeConverter<ICloudBlob> _converter;

        public BlobBinding(IArgumentBinding<ICloudBlob> argumentBinding, CloudStorageAccount account, string containerName, string blobName)
        {
            _argumentBinding = argumentBinding;
            _client = account.CreateCloudBlobClient();
            _containerName = containerName;
            _blobName = blobName;
            _converter = CreateConverter(_client);
        }

        private static IObjectToTypeConverter<ICloudBlob> CreateConverter(CloudBlobClient client)
        {
            return new CompositeObjectToTypeConverter<ICloudBlob>(
                new OutputConverter<ICloudBlob>(new IdentityConverter<ICloudBlob>()),
                new OutputConverter<string>(new StringToCloudBlobConverter(client)));
        }

        public string ContainerName
        {
            get { return _containerName; }
        }

        public string BlobName
        {
            get { return _containerName; }
        }

        public string BlobPath
        {
            get { return _containerName + "/" + _blobName; }
        }

        private IValueProvider Bind(ICloudBlob value, ArgumentBindingContext context)
        {
            return _argumentBinding.Bind(value, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            string resolvedPath = RouteParser.ApplyBindingData(BlobPath, context.BindingData);
            CloudBlobPath parsedResolvedPath = new CloudBlobPath(resolvedPath);
            CloudBlobContainer container = _client.GetContainerReference(parsedResolvedPath.ContainerName);
            ICloudBlob blob = container.GetBlobReferenceFromServer(parsedResolvedPath.BlobName);
            return Bind(blob, context);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            ICloudBlob blob = null;

            if (!_converter.TryConvert(value, out blob))
            {
                throw new InvalidOperationException("Unable to convert trigger to ICloudBlob.");
            }

            return Bind(blob, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BlobParameterDescriptor
            {
                ContainerName = _containerName,
                BlobName = _blobName,
                IsInput = true
            };
        }
    }
}
