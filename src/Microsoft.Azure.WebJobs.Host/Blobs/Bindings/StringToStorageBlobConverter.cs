// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class StringToStorageBlobConverter : IAsyncConverter<string, IStorageBlob>
    {
        private readonly IStorageBlobClient _client;
        private readonly IBindableBlobPath _defaultPath;
        private readonly Type _argumentType;

        public StringToStorageBlobConverter(IStorageBlobClient client, IBindableBlobPath defaultPath, Type argumentType)
        {
            _client = client;
            _defaultPath = defaultPath;
            _argumentType = argumentType;
        }

        public async Task<IStorageBlob> ConvertAsync(string input, CancellationToken cancellationToken)
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

            IStorageBlobContainer container = _client.GetContainerReference(path.ContainerName);
            await container.CreateIfNotExistsAsync(cancellationToken);
            return await container.GetBlobReferenceForArgumentTypeAsync(path.BlobName, _argumentType,
                cancellationToken);
        }
    }
}
