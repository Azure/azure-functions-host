// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class StringToCloudBlobConverter : IAsyncConverter<string, ICloudBlob>
    {
        private readonly CloudBlobClient _client;
        private readonly IBindableBlobPath _defaultPath;
        private readonly Type _argumentType;

        public StringToCloudBlobConverter(CloudBlobClient client, IBindableBlobPath defaultPath, Type argumentType)
        {
            _client = client;
            _defaultPath = defaultPath;
            _argumentType = argumentType;
        }

        public Task<ICloudBlob> ConvertAsync(string input, CancellationToken cancellationToken)
        {
            BlobPath path;

            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input) && _defaultPath.IsBound)
            {
                path = _defaultPath.Bind(null);
            }
            else
            {
                path = BlobPath.ParseAndValidate(input);
            }

            CloudBlobContainer container = _client.GetContainerReference(path.ContainerName);
            container.CreateIfNotExists();
            return container.GetBlobReferenceForArgumentTypeAsync(path.BlobName, _argumentType, cancellationToken);
        }
    }
}
