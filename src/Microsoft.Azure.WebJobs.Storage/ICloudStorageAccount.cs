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

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
#if PUBLICSTORAGE
    /// <summary>Defines a cloud storage account.</summary>
    [CLSCompliant(false)]
    public interface ICloudStorageAccount
#else
    internal interface ICloudStorageAccount
#endif
    {
        /// <summary>Creates a queue client.</summary>
        /// <returns>A queue client.</returns>
        ICloudQueueClient CreateCloudQueueClient();

        /// <summary>
        /// Creates a table client.</summary>
        /// <returns>A table client.</returns>
        ICloudTableClient CreateCloudTableClient();
    }
}
