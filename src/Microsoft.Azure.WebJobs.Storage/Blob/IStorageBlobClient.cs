// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Auth;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines a blob client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageBlobClient
#else
    internal interface IStorageBlobClient
#endif
    {
        /// <summary>Gets the credentials used to connect to the account.</summary>
        StorageCredentials Credentials { get; }

        /// <summary>Gets a container reference.</summary>
        /// <param name="containerName">The container name.</param>
        /// <returns>A container reference.</returns>
        IStorageBlobContainer GetContainerReference(string containerName);
    }
}
