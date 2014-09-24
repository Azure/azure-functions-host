// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
#if PUBLICSTORAGE
using Microsoft.Azure.WebJobs.Storage.Queue;
using Microsoft.Azure.WebJobs.Storage.Table;
#else
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
        /// <summary>Gets the credentials used to connect to the account.</summary>
        StorageCredentials Credentials { get; }

        /// <summary>Gets the underlying <see cref="CloudStorageAccount"/>.</summary>
        CloudStorageAccount SdkObject { get; }

        /// <summary>Creates a queue client.</summary>
        /// <returns>A queue client.</returns>
        IStorageQueueClient CreateQueueClient();

        /// <summary>
        /// Creates a table client.</summary>
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
