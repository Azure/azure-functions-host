// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines the system properties of a blob.</summary>
#if PUBLICSTORAGE
    public interface IStorageBlobProperties
#else
    internal interface IStorageBlobProperties
#endif
    {
        /// <summary>Gets the ETag of the blob.</summary>
        string ETag { get; }

        /// <summary>Gets the last time the blob was modified.</summary>
        DateTimeOffset? LastModified { get; }
    }
}
