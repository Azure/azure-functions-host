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
    /// <summary>Defines a segment of blob list results.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlobResultSegment
#else
    internal interface IStorageBlobResultSegment
#endif
    {
        /// <summary>Gets the continuation token to use to retrieve the next segment of results.</summary>
        BlobContinuationToken ContinuationToken { get; }

        /// <summary>Gets the results in this segment.</summary>
        IEnumerable<IStorageListBlobItem> Results { get; }
    }
}
