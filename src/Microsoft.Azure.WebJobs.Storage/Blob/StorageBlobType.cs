// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines blob types.</summary>
#if PUBLICSTORAGE
    public enum StorageBlobType
#else
    internal enum StorageBlobType
#endif
    {
        /// <summary>A block blob.</summary>
        BlockBlob = BlobType.BlockBlob,

        /// <summary>A page blob.</summary>
        PageBlob = BlobType.PageBlob,

        /// <summary>An append blob.</summary>
        AppendBlob = BlobType.AppendBlob
    }
}
