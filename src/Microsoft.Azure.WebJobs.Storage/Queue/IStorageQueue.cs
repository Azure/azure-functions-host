// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
    /// <summary>Defines a queue.</summary>
#if PUBLICSTORAGE
    
    public interface IStorageQueue
#else
    internal interface IStorageQueue
#endif
    {
        /// <summary>Gets the name of the queue.</summary>
        string Name { get; }

        /// <summary>Gets the underlying <see cref="CloudQueue"/>.</summary>
        CloudQueue SdkObject { get; }

        /// <summary>Gets the queue service client.</summary>
        IStorageQueueClient ServiceClient { get; }

        /// <summary>Adds a message to the queue.</summary>
        /// <param name="message">The message to enqueue.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>A task that will add the message to the queue.</remarks>
        Task AddMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken);

        /// <summary>Creates the queue if it does not already exist.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will create the queue if it does not already exist.</returns>
        Task CreateIfNotExistsAsync(CancellationToken cancellationToken);

        /// <summary>Creates a new queue message.</summary>
        /// <param name="content">The message content.</param>
        /// <returns>A new queue message</returns>
        IStorageQueueMessage CreateMessage(byte[] content);

        /// <summary>Creates a new queue message.</summary>
        /// <param name="content">The message content.</param>
        /// <returns>A new queue message</returns>
        IStorageQueueMessage CreateMessage(string content);

        /// <summary>Deletes a message.</summary>
        /// <param name="message">The message to delete.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will delete the message.</returns>
        Task DeleteMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken);

        /// <summary>Determines whether the queue exists.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will determine whether the queue exists.</returns>
        Task<bool> ExistsAsync(CancellationToken cancellationToken);

        /// <summary>Gets messages from the queue and marks them invisible during the visibility timeout.</summary>
        /// <param name="messageCount">The number of messages to retrieve.</param>
        /// <param name="visibilityTimeout">The visibility timeout.</param>
        /// <param name="options">The options for the request.</param>
        /// <param name="operationContext">The operation context for the request.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task that will get messages from the queue and marks them invisible during the visibility timeout.
        /// </returns>
        Task<IEnumerable<IStorageQueueMessage>> GetMessagesAsync(int messageCount, TimeSpan? visibilityTimeout,
            QueueRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken);

        /// <summary>Updates the visibility timeout and optionally the content of a message.</summary>
        /// <param name="message">The message to update.</param>
        /// <param name="visibilityTimeout">The visibility timeout.</param>
        /// <param name="updateFields">
        /// <see cref="MessageUpdateFields"/> flags specifying which parts of the message to update.
        /// </param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will update the message.</returns>
        Task UpdateMessageAsync(IStorageQueueMessage message, TimeSpan visibilityTimeout,
            MessageUpdateFields updateFields, CancellationToken cancellationToken);
    }
}
