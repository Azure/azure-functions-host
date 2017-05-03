// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines the system properties of a blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlobProperties
#else
    internal interface IStorageBlobProperties
#endif
    {
        /// <summary>
        /// Gets the ETag of the blob.
        /// </summary>
        string ETag { get; }

        /// <summary>
        /// Gets the last time the blob was modified.
        /// </summary>
        DateTimeOffset? LastModified { get; }

        /// <summary>
        /// Gets the LeaseState of the blob.
        /// </summary>
        LeaseState LeaseState { get; }

        /// <summary>
        /// Gets the LeaseStatus of the blob.
        /// </summary>
        LeaseStatus LeaseStatus { get; }

        /// <summary>
        /// Gets the underlying SDK object.
        /// </summary>
        BlobProperties SdkObject { get; }
    }
}
