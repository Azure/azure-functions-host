// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class BlobBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IBlobArgumentBinding _argumentBinding;
        private readonly IStorageBlobClient _client;
        private readonly string _accountName;
        private readonly IBindableBlobPath _path;
        private readonly IAsyncObjectToTypeConverter<IStorageBlob> _converter;

        public BlobBinding(string parameterName, IBlobArgumentBinding argumentBinding, IStorageBlobClient client,
            IBindableBlobPath path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = BlobClient.GetAccountName(client);
            _path = path;
            _converter = CreateConverter(_client, path, argumentBinding.ValueType);
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        public string ContainerName
        {
            get { return _path.ContainerNamePattern; }
        }

        public string BlobName
        {
            get { return _path.BlobNamePattern; }
        }

        public IBindableBlobPath Path
        {
            get { return _path; }
        }

        public FileAccess Access
        {
            get { return _argumentBinding.Access; }
        }

        private static IAsyncObjectToTypeConverter<IStorageBlob> CreateConverter(IStorageBlobClient client,
            IBindableBlobPath path, Type argumentType)
        {
            return new CompositeAsyncObjectToTypeConverter<IStorageBlob>(
                new OutputConverter<IStorageBlob>(new AsyncConverter<IStorageBlob, IStorageBlob>(
                    new IdentityConverter<IStorageBlob>())),
                new OutputConverter<ICloudBlob>(new AsyncConverter<ICloudBlob, IStorageBlob>(
                    new CloudBlobToStorageBlobConverter())),
                new OutputConverter<string>(new StringToStorageBlobConverter(client, path, argumentType)));
        }

        private Task<IValueProvider> BindBlobAsync(IStorageBlob value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        public async Task<IValueProvider> BindAsync(BindingContext context)
        {
            BlobPath boundPath = _path.Bind(context.BindingData);
            IStorageBlobContainer container = _client.GetContainerReference(boundPath.ContainerName);
            
            if (_argumentBinding.Access != FileAccess.Read)
            {
                await container.CreateIfNotExistsAsync(context.CancellationToken);
            }

            Type argumentType = _argumentBinding.ValueType;
            string blobName = boundPath.BlobName;
            IStorageBlob blob = await container.GetBlobReferenceForArgumentTypeAsync(blobName, argumentType,
                context.CancellationToken);

            return await BindBlobAsync(blob, context.ValueContext);
        }

        public async Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            ConversionResult<IStorageBlob> conversionResult =
                await _converter.TryConvertAsync(value, context.CancellationToken);

            if (!conversionResult.Succeeded)
            {
                throw new InvalidOperationException("Unable to convert value to ICloudBlob.");
            }

            return await BindBlobAsync(conversionResult.Result, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BlobParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                ContainerName = _path.ContainerNamePattern,
                BlobName = _path.BlobNamePattern,
                Access = _argumentBinding.Access
            };
        }
    }
}
