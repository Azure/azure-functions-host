// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
#if PUBLICSTORAGE
using Microsoft.Azure.WebJobs.Storage.Blob;
using Microsoft.Azure.WebJobs.Storage.Queue;
using Microsoft.Azure.WebJobs.Storage.Table;
#else
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
#endif
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
#if PUBLICSTORAGE
    /// <summary>Defines a storage account.</summary>
    [CLSCompliant(false)]
    public interface IStorageAccount
#else
    internal interface IStorageAccount
#endif
    {
        /// <summary>Gets the primary endpoint for the blob service.</summary>
        Uri BlobEndpoint { get; }

        /// <summary>Gets the credentials used to connect to the account.</summary>
        StorageCredentials Credentials { get; }

        /// <summary>Gets the underlying <see cref="CloudStorageAccount"/>.</summary>
        CloudStorageAccount SdkObject { get; }

        /// <summary>Creates a blob client.</summary>
        /// <returns>A blob client.</returns>
        IStorageBlobClient CreateBlobClient();

        /// <summary>Creates a queue client.</summary>
        /// <returns>A queue client.</returns>
        IStorageQueueClient CreateQueueClient();

        /// <summary>Creates a table client.</summary>
        /// <returns>A table client.</returns>
        IStorageTableClient CreateTableClient();

        /// <summary>Gets the connection string for the storage account.</summary>
        /// <param name="exportSecrets">
        /// <see langword="true"/> to include credentials in the connection string; otherwise, <see langword="false"/>.
        /// </param>
        /// <returns>The connection string for the storage account.</returns>
        string ToString(bool exportSecrets);
    }
}
