// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal sealed class PollQueueCommand : IRecurrentCommand
    {
        private const int PoisonThreshold = 5;

        private readonly CloudQueue _queue;
        private readonly CloudQueue _poisonQueue;
        private readonly ITriggerExecutor<CloudQueueMessage> _triggerExecutor;

        public PollQueueCommand(CloudQueue queue, CloudQueue poisonQueue,
            ITriggerExecutor<CloudQueueMessage> triggerExecutor)
        {
            _queue = queue;
            _poisonQueue = poisonQueue;
            _triggerExecutor = triggerExecutor;
        }

        public async Task<bool> TryExecuteAsync(CancellationToken cancellationToken)
        {
            if (!await _queue.ExistsAsync(cancellationToken))
            {
                // Back off when no message is available.
                return false;
            }

            // What if job takes longer. Call CloudQueue.UpdateMessage
            TimeSpan visibilityTimeout = TimeSpan.FromMinutes(10); // long enough to process the job
            CloudQueueMessage message;
            bool foundMessage = false;

            do
            {
                try
                {
                    message = await _queue.GetMessageAsync(visibilityTimeout,
                        options: null,
                        operationContext: null,
                        cancellationToken: cancellationToken);
                }
                catch (StorageException exception)
                {
                    if (exception.IsNotFoundQueueNotFound() ||
                        exception.IsConflictQueueBeingDeletedOrDisabled() ||
                        exception.IsServerSideError())
                    {
                        // Back off when no message is available.
                        return false;
                    }
                    else
                    {
                        throw;
                    }
                }

                if (message != null)
                {
                    foundMessage = true;
                    bool succeeded;

                    using (ITaskSeriesTimer timer = CreateUpdateMessageVisibilityTimer(_queue, message,
                        visibilityTimeout))
                    {
                        timer.Start();

                        succeeded = await _triggerExecutor.ExecuteAsync(message, cancellationToken);
                    }

                    // Need to call Delete message only if function succeeded.
                    if (succeeded)
                    {
                        await DeleteMessageAsync(message, cancellationToken);
                    }
                    else if (_poisonQueue != null)
                    {
                        if (message.DequeueCount >= PoisonThreshold)
                        {
                            Console.WriteLine("Queue poison message threshold exceeded. Moving message to queue '{0}'.",
                                _poisonQueue.Name);
                            await CopyToPoisonQueueAsync(message, cancellationToken);
                            await DeleteMessageAsync(message, cancellationToken);
                        }
                        else
                        {
                            await ReleaseMessageAsync(message, cancellationToken);
                        }
                    }
                    else
                    {
                        // For queues without a corresponding poison queue, leave the message invisible when processing
                        // fails to prevent a fast infinite loop.
                        // Specifically, don't call ReleaseMessage(message)
                    }
                }
            } while (message != null);

            // Back off when no message was found.
            return foundMessage;
        }

        private static ITaskSeriesTimer CreateUpdateMessageVisibilityTimer(CloudQueue queue,
            CloudQueueMessage message, TimeSpan visibilityTimeout)
        {
            // Update a message's visibility when it is halfway to expiring.
            TimeSpan normalUpdateInterval = new TimeSpan(visibilityTimeout.Ticks / 2);

            IRecurrentCommand command = new UpdateQueueMessageVisibilityCommand(queue, message, visibilityTimeout);
            return LinearSpeedupStrategy.CreateTimer(command, normalUpdateInterval, TimeSpan.FromMinutes(1));
        }

        private async Task DeleteMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
        {
            try
            {
                await _queue.DeleteMessageAsync(message, cancellationToken);
            }
            catch (StorageException exception)
            {
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

        private async Task ReleaseMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
        {
            try
            {
                // We couldn't process the message. Let someone else try.
                await _queue.UpdateMessageAsync(message, TimeSpan.Zero, MessageUpdateFields.Visibility, cancellationToken);
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

        private Task CopyToPoisonQueueAsync(CloudQueueMessage message, CancellationToken cancellationToken)
        {
            return _poisonQueue.AddMessageAndCreateIfNotExistsAsync(message, cancellationToken);
        }
    }
}
