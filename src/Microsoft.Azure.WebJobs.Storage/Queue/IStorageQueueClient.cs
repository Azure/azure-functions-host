// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Queue
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Queue
#endif
{
    /// <summary>Defines a queue client.</summary>
#if PUBLICSTORAGE
    
    public interface IStorageQueueClient
#else
    internal interface IStorageQueueClient
#endif
    {
        /// <summary>Gets the credentials used to connect to the account.</summary>
        StorageCredentials Credentials { get; }

        /// <summary>Gets the underlying SDK client.</summary>
        CloudQueueClient SdkObject { get; }

        /// <summary>Gets a queue reference.</summary>
        /// <param name="queueName">The queue name.</param>
        /// <returns>A queue reference.</returns>
        IStorageQueue GetQueueReference(string queueName);     
    }
}
