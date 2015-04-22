// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents a segment of blob list results.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageBlobResultSegment : IStorageBlobResultSegment
#else
    internal class StorageBlobResultSegment : IStorageBlobResultSegment
#endif
    {
        private readonly BlobContinuationToken _continuationToken;
        private readonly IEnumerable<IStorageListBlobItem> _results;

        /// <summary>Initializes a new instance of the <see cref="StorageBlobResultSegment"/> class.</summary>
        /// <param name="continuationToken">The continuation token.</param>
        /// <param name="results">The results in the segment.</param>
        public StorageBlobResultSegment(BlobContinuationToken continuationToken,
            IEnumerable<IStorageListBlobItem> results)
        {
            _continuationToken = continuationToken;
            _results = results;
        }

        /// <inheritdoc />
        public BlobContinuationToken ContinuationToken
        {
            get { return _continuationToken; }
        }

        /// <inheritdoc />
        public IEnumerable<IStorageListBlobItem> Results
        {
            get { return _results; }
        }
    }
}
