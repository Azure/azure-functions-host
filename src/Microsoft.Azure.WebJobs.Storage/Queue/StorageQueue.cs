// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Queue
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Queue
#endif
{
    /// <summary>Represents a queue.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageQueue : IStorageQueue
#else
    internal class StorageQueue : IStorageQueue
#endif
    {
        private readonly IStorageQueueClient _parent;
        private readonly CloudQueue _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageQueue"/> class.</summary>
        /// <param name="parent">The parent queue client.</param>
        /// <param name="sdk">The SDK queue to wrap.</param>
        public StorageQueue(IStorageQueueClient parent, CloudQueue sdk)
        {
            _parent = parent;
            _sdk = sdk;
        }

        /// <inheritdoc />
        public string Name
        {
            get { return _sdk.Name; }
        }

        /// <inheritdoc />
        public CloudQueue SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public IStorageQueueClient ServiceClient
        {
            get { return _parent; }
        }

        /// <inheritdoc />
        public Task AddMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
        {
            CloudQueueMessage sdkMessage = ((StorageQueueMessage)message).SdkObject;
            return _sdk.AddMessageAsync(sdkMessage, cancellationToken);
        }

        /// <inheritdoc />
        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.CreateIfNotExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public IStorageQueueMessage CreateMessage(byte[] content)
        {
            return new StorageQueueMessage(new CloudQueueMessage(content));
        }

        /// <inheritdoc />
        public IStorageQueueMessage CreateMessage(string content)
        {
            return new StorageQueueMessage(new CloudQueueMessage(content));
        }

        /// <inheritdoc />
        public Task DeleteMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
        {
            CloudQueueMessage sdkMessage = ((StorageQueueMessage)message).SdkObject;
            return _sdk.DeleteMessageAsync(sdkMessage, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.ExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<IStorageQueueMessage>> GetMessagesAsync(int messageCount, TimeSpan? visibilityTimeout,
            QueueRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
        {
            Task<IEnumerable<CloudQueueMessage>> innerTask = _sdk.GetMessagesAsync(messageCount,
                visibilityTimeout, options, operationContext, cancellationToken);
            return GetMessagesAsyncCore(innerTask);
        }

        private static async Task<IEnumerable<IStorageQueueMessage>> GetMessagesAsyncCore(
            Task<IEnumerable<CloudQueueMessage>> innerTask)
        {
            IEnumerable<CloudQueueMessage> sdkMessages = await innerTask;
            return sdkMessages.Select<CloudQueueMessage, IStorageQueueMessage>(m => new StorageQueueMessage(m));
        }

        /// <inheritdoc />
        public Task UpdateMessageAsync(IStorageQueueMessage message, TimeSpan visibilityTimeout,
            MessageUpdateFields updateFields, CancellationToken cancellationToken)
        {
            CloudQueueMessage sdkMessage = ((StorageQueueMessage)message).SdkObject;
            return _sdk.UpdateMessageAsync(sdkMessage, visibilityTimeout, updateFields, cancellationToken);
        }
    }
}
