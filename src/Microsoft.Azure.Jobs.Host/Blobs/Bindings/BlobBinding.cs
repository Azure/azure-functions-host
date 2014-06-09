using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class BlobBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IBlobArgumentBinding _argumentBinding;
        private readonly CloudBlobClient _client;
        private readonly string _accountName;
        private readonly string _containerName;
        private readonly string _blobName;
        private readonly IObjectToTypeConverter<ICloudBlob> _converter;

        public BlobBinding(string parameterName, IBlobArgumentBinding argumentBinding, CloudBlobClient client,
            string containerName, string blobName)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = BlobClient.GetAccountName(client);
            _containerName = containerName;
            _blobName = blobName;
            _converter = CreateConverter(_client, containerName, blobName, argumentBinding.ValueType);
        }

        public bool FromAttribute
        {
            get { return true; }
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

        public FileAccess Access
        {
            get { return _argumentBinding.Access; }
        }

        private static IObjectToTypeConverter<ICloudBlob> CreateConverter(CloudBlobClient client, string containerName,
            string blobName, Type argumentType)
        {
            return new CompositeObjectToTypeConverter<ICloudBlob>(
                new OutputConverter<ICloudBlob>(new IdentityConverter<ICloudBlob>()),
                new OutputConverter<string>(new StringToCloudBlobConverter(client, containerName, blobName,
                    argumentType)));
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
            container.CreateIfNotExists();

            Type argumentType = _argumentBinding.ValueType;
            string blobName = parsedResolvedPath.BlobName;
            ICloudBlob blob = container.GetBlobReferenceForArgumentType(blobName, argumentType);

            return Bind(blob, context);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            ICloudBlob blob = null;

            if (!_converter.TryConvert(value, out blob))
            {
                throw new InvalidOperationException("Unable to convert value to ICloudBlob.");
            }

            return Bind(blob, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BlobParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                ContainerName = _containerName,
                BlobName = _blobName,
                Access = _argumentBinding.Access
            };
        }
    }
}
