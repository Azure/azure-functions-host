// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    /// <summary>
    /// This class defines a strategy used for processing queue messages.
    /// </summary>
    /// <remarks>
    /// Custom <see cref="QueueProcessor"/> implementations can be registered by implementing
    /// a custom <see cref="IQueueProcessorFactory"/> and setting it via <see cref="JobHostQueuesConfiguration.QueueProcessorFactory"/>.
    /// </remarks>
    [CLSCompliant(false)]
    public class QueueProcessor
    {
        private readonly CloudQueue _queue;
        private readonly CloudQueue _poisonQueue;
        private readonly TraceWriter _trace;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="context">The factory context.</param>
        public QueueProcessor(QueueProcessorFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _queue = context.Queue;
            _poisonQueue = context.PoisonQueue;
            _trace = context.Trace;

            MaxDequeueCount = context.MaxDequeueCount;
            BatchSize = context.BatchSize;
            NewBatchThreshold = context.NewBatchThreshold;
            VisibilityTimeout = context.VisibilityTimeout;
        }

        /// <summary>
        /// Event raised when a message is added to the poison queue.
        /// </summary>
        public event EventHandler MessageAddedToPoisonQueue;

        /// <summary>
        /// Gets or sets the number of queue messages to retrieve and process in parallel.
        /// </summary>
        public int BatchSize { get; protected set; }

        /// <summary>
        /// Gets or sets the number of times to try processing a message before moving it to the poison queue.
        /// </summary>
        public int MaxDequeueCount { get; protected set; }

        /// <summary>
        /// Gets or sets the threshold at which a new batch of messages will be fetched.
        /// </summary>
        public int NewBatchThreshold { get; protected set; }

        /// <summary>
        /// Gets or sets the default message visibility timeout that will be used
        /// for messages that fail processing.
        /// </summary>
        public TimeSpan VisibilityTimeout { get; protected set; }

        /// <summary>
        /// This method is called when there is a new message to process, before the job function is invoked.
        /// This allows any preprocessing to take place on the message before processing begins.
        /// </summary>
        /// <param name="message">The message to process.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use</param>
        /// <returns>True if the message processing should continue, false otherwise.</returns>
        public virtual async Task<bool> BeginProcessingMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
        {
            return await Task.FromResult<bool>(true);
        }

        /// <summary>
        /// This method completes processing of the specified message, after the job function has been invoked.
        /// </summary>
        /// <remarks>
        /// If the message was processed successfully, the message should be deleted. If message processing failed, the
        /// message should be release back to the queue, or if the maximum dequeue count has been exceeded, the message
        /// should be moved to the poison queue (if poison queue handling is configured for the queue).
        /// </remarks>
        /// <param name="message">The message to complete processing for.</param>
        /// <param name="result">The <see cref="FunctionResult"/> from the job invocation.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use</param>
        /// <returns></returns>
        public virtual async Task CompleteProcessingMessageAsync(CloudQueueMessage message, FunctionResult result, CancellationToken cancellationToken)
        {            
            if (result.Succeeded)
            {
                await DeleteMessageAsync(message, cancellationToken);
            }
            else if (_poisonQueue != null)
            {
                if (message.DequeueCount >= MaxDequeueCount)
                {
                    await CopyMessageToPoisonQueueAsync(message, cancellationToken);
                    await DeleteMessageAsync(message, cancellationToken);
                }
                else
                {
                    await ReleaseMessageAsync(message, result, VisibilityTimeout, cancellationToken);
                }
            }
            else
            {
                // For queues without a corresponding poison queue, leave the message invisible when processing
                // fails to prevent a fast infinite loop.
                // Specifically, don't call ReleaseMessage(message)
            }
        }

        /// <summary>
        /// Moves the specified message to the poison queue.
        /// </summary>
        /// <param name="message">The poison message</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use</param>
        /// <returns></returns>
        protected virtual async Task CopyMessageToPoisonQueueAsync(CloudQueueMessage message, CancellationToken cancellationToken)
        {
            _trace.Warning(string.Format(CultureInfo.InvariantCulture, "Message has reached MaxDequeueCount of {0}. Moving message to queue '{1}'.", MaxDequeueCount, _poisonQueue.Name), TraceSource.Execution);

            await AddMessageAndCreateIfNotExistsAsync(_poisonQueue, message, cancellationToken);

            OnMessageAddedToPoisonQueue(EventArgs.Empty);
        }

        /// <summary>
        /// Release the specified failed message back to the queue.
        /// </summary>
        /// <param name="message">The message to release</param>
        /// <param name="result">The <see cref="FunctionResult"/> from the job invocation.</param>
        /// <param name="visibilityTimeout">The visibility timeout to set for the message</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use</param>
        /// <returns></returns>
        protected virtual async Task ReleaseMessageAsync(CloudQueueMessage message, FunctionResult result, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
        {
            try
            {
                // We couldn't process the message. Let someone else try.
                await _queue.UpdateMessageAsync(message, visibilityTimeout, MessageUpdateFields.Visibility, cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsBadRequestPopReceiptMismatch())
                {
                    // Someone else already took over the message; no need to do anything.
                    return;
                }
                else if (exception.IsNotFoundMessageOrQueueNotFound() ||
                         exception.IsConflictQueueBeingDeletedOrDisabled())
                {
                    // The message or queue is gone, or the queue is down; no need to release the message.
                    return;
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Delete the specified message.
        /// </summary>
        /// <param name="message">The message to delete.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use</param>
        /// <returns></returns>
        protected virtual async Task DeleteMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
        {
            try
            {
                await _queue.DeleteMessageAsync(message, cancellationToken);
            }
            catch (StorageException exception)
            {
                // For consistency, the exceptions handled here should match UpdateQueueMessageVisibilityCommand.
                if (exception.IsBadRequestPopReceiptMismatch())
                {
                    // If someone else took over the message; let them delete it.
                    return;
                }
                else if (exception.IsNotFoundMessageOrQueueNotFound() ||
                         exception.IsConflictQueueBeingDeletedOrDisabled())
                {
                    // The message or queue is gone, or the queue is down; no need to delete the message.
                    return;
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Called to raise the MessageAddedToPoisonQueue event
        /// </summary>
        /// <param name="e">The event arguments</param>
        protected internal virtual void OnMessageAddedToPoisonQueue(EventArgs e)
        {
            EventHandler handler = MessageAddedToPoisonQueue;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private static async Task AddMessageAndCreateIfNotExistsAsync(CloudQueue queue, CloudQueueMessage message, CancellationToken cancellationToken)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            bool isQueueNotFoundException = false;
            try
            {
                await queue.AddMessageAsync(message, cancellationToken);
                return;
            }
            catch (StorageException exception)
            {
                if (!exception.IsNotFoundQueueNotFound())
                {
                    throw;
                }

                isQueueNotFoundException = true;
            }

            Debug.Assert(isQueueNotFoundException);
            await queue.CreateIfNotExistsAsync(cancellationToken);
            await queue.AddMessageAsync(message, cancellationToken);
        }
    }
}
