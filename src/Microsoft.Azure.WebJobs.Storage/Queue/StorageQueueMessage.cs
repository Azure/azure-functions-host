// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Queue;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Queue
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Queue
#endif
{
    /// <summary>Represents a queue message.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageQueueMessage : IStorageQueueMessage
#else
    internal class StorageQueueMessage : IStorageQueueMessage
#endif
    {
        private readonly CloudQueueMessage _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageQueueMessage"/> class.</summary>
        /// <param name="message">The SDK message to wrap.</param>
        public StorageQueueMessage(CloudQueueMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            _sdk = message;
        }

        /// <inheritdoc />
        public byte[] AsBytes
        {
            get { return _sdk.AsBytes; }
        }

        /// <inheritdoc />
        public string AsString
        {
            get { return _sdk.AsString; }
        }

        /// <inheritdoc />
        public int DequeueCount
        {
            get { return _sdk.DequeueCount; }
        }

        /// <inheritdoc />
        public DateTimeOffset? ExpirationTime
        {
            get { return _sdk.ExpirationTime; }
        }

        /// <inheritdoc />
        public string Id
        {
            get { return _sdk.Id; }
        }

        /// <inheritdoc />
        public DateTimeOffset? InsertionTime
        {
            get { return _sdk.InsertionTime; }
        }

        /// <inheritdoc />
        public DateTimeOffset? NextVisibleTime
        {
            get { return _sdk.NextVisibleTime; }
        }

        /// <inheritdoc />
        public string PopReceipt
        {
            get { return _sdk.PopReceipt; }
        }

        /// <inheritdoc />
        public CloudQueueMessage SdkObject
        {
            get { return _sdk; }
        }
    }
}
