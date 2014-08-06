// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
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
        private readonly IBindableBlobPath _path;
        private readonly IAsyncObjectToTypeConverter<ICloudBlob> _converter;

        public BlobBinding(string parameterName, IBlobArgumentBinding argumentBinding, CloudBlobClient client,
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

        private static IAsyncObjectToTypeConverter<ICloudBlob> CreateConverter(CloudBlobClient client,
            IBindableBlobPath path, Type argumentType)
        {
            return new CompositeAsyncObjectToTypeConverter<ICloudBlob>(
                new OutputConverter<ICloudBlob>(new AsyncIdentityConverter<ICloudBlob>()),
                new OutputConverter<string>(new StringToCloudBlobConverter(client, path, argumentType)));
        }

        private Task<IValueProvider> BindAsync(ICloudBlob value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        public async Task<IValueProvider> BindAsync(BindingContext context)
        {
            BlobPath boundPath = _path.Bind(context.BindingData);
            CloudBlobContainer container = _client.GetContainerReference(boundPath.ContainerName);
            container.CreateIfNotExists();

            Type argumentType = _argumentBinding.ValueType;
            string blobName = boundPath.BlobName;
            ICloudBlob blob = await container.GetBlobReferenceForArgumentTypeAsync(blobName, argumentType,
                context.CancellationToken);

            return await BindAsync(blob, context.ValueContext);
        }

        public async Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            ConversionResult<ICloudBlob> conversionResult =
                await _converter.TryConvertAsync(value, context.CancellationToken);

            if (!conversionResult.Succeeded)
            {
                throw new InvalidOperationException("Unable to convert value to ICloudBlob.");
            }

            return await BindAsync(conversionResult.Result, context);
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
