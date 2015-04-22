// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Queue;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Queue
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Queue
#endif
{
    /// <summary>Defines a queue message.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageQueueMessage
#else
    internal interface IStorageQueueMessage
#endif
    {
        /// <summary>Get the message contents as a byte array.</summary>
        byte[] AsBytes { get; }

        /// <summary>Gets the message contents as text.</summary>
        string AsString { get; }

        /// <summary>Gets the number of times the message has been dequeued.</summary>
        int DequeueCount { get; }

        /// <summary>Gets the time at which the message expires.</summary>
        DateTimeOffset? ExpirationTime { get; }

        /// <summary>Gets the message ID.</summary>
        string Id { get; }

        /// <summary>Gets the time at which the message was added to the queue.</summary>
        DateTimeOffset? InsertionTime { get; }

        /// <summary>Gets the next time at which the message will be visible.</summary>
        DateTimeOffset? NextVisibleTime { get; }

        /// <summary>Gets the pop receipt for the message.</summary>
        string PopReceipt { get; }

        /// <summary>Gets the underlying SDK queue message.</summary>
        CloudQueueMessage SdkObject { get; }
    }
}
