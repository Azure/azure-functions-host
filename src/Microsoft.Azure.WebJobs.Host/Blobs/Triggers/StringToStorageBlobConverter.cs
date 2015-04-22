// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Triggers
{
    internal class StringToStorageBlobConverter : IAsyncConverter<string, IStorageBlob>
    {
        private readonly IStorageBlobClient _client;

        public StringToStorageBlobConverter(IStorageBlobClient client)
        {
            _client = client;
        }

        public Task<IStorageBlob> ConvertAsync(string input, CancellationToken cancellationToken)
        {
            BlobPath path = BlobPath.ParseAndValidate(input);
            IStorageBlobContainer container = _client.GetContainerReference(path.ContainerName);
            return container.GetBlobReferenceFromServerAsync(path.BlobName, cancellationToken);
        }
    }
}
