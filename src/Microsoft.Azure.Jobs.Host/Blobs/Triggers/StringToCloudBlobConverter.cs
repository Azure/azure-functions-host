// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
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
            BlobPath path = BlobPath.ParseAndValidate(input);
            CloudBlobContainer container = _client.GetContainerReference(path.ContainerName);
            return container.GetBlobReferenceFromServer(path.BlobName);
        }
    }
}
