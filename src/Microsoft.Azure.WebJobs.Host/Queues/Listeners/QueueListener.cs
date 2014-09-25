// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal sealed class QueueListener : IListener, ITaskSeriesCommand, INotificationCommand
    {
        private readonly ITaskSeriesTimer _timer;
        private readonly IDelayStrategy _delayStrategy;
        private readonly IStorageQueue _queue;
        private readonly IStorageQueue _poisonQueue;
        private readonly ITriggerExecutor<IStorageQueueMessage> _triggerExecutor;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IMessageEnqueuedWatcher _sharedWatcher;
        private readonly int _batchSize;
        private readonly uint _newBatchThreshold;
        private readonly uint _maxDequeueCount;
        private readonly List<Task> _processing = new List<Task>();
        private readonly object _stopWaitingTaskSourceLock = new object();

        private bool _foundMessageSinceLastDelay;
        private bool _disposed;
        private TaskCompletionSource<object> _stopWaitingTaskSource;

        public QueueListener(IStorageQueue queue,
            IStorageQueue poisonQueue,
            ITriggerExecutor<IStorageQueueMessage> triggerExecutor,
            IDelayStrategy delayStrategy,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            SharedQueueWatcher sharedWatcher,
            int batchSize,
            int maxDequeueCount)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException("batchSize");
            }

            if (maxDequeueCount <= 0)
            {
                throw new ArgumentOutOfRangeException("maxDequeueCount");
            }

            _timer = new TaskSeriesTimer(this, backgroundExceptionDispatcher, Task.Delay(0));
            _queue = queue;
            _poisonQueue = poisonQueue;
            _triggerExecutor = triggerExecutor;
            _delayStrategy = delayStrategy;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;

            if (sharedWatcher != null)
            {
                // Call Notify whenever a function adds a message to this queue.
                sharedWatcher.Register(queue.Name, this);
                _sharedWatcher = sharedWatcher;
            }

            _batchSize = batchSize;
            _newBatchThreshold = (uint)_batchSize / 2;
            _maxDequeueCount = (uint)maxDequeueCount;
        }


        public void Cancel()
        {
            ThrowIfDisposed();
            _timer.Cancel();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _timer.Start();
            return Task.FromResult(0);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _timer.Cancel();
            await Task.WhenAll(_processing);
            await _timer.StopAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer.Dispose();
                _disposed = true;
            }
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            lock (_stopWaitingTaskSourceLock)
            {
                if (_stopWaitingTaskSource != null)
                {
                    _stopWaitingTaskSource.TrySetResult(null);
                }

                _stopWaitingTaskSource = new TaskCompletionSource<object>();
            }

            if (!await _queue.ExistsAsync(cancellationToken))
            {
                // Back off when no message is available.
                return CreateBackoffResult();
            }

            // What if job takes longer. Call CloudQueue.UpdateMessage
            TimeSpan visibilityTimeout = TimeSpan.FromMinutes(10); // long enough to process the job
            IEnumerable<IStorageQueueMessage> batch;

            try
            {
                batch = await _queue.GetMessagesAsync(_batchSize,
                    visibilityTimeout,
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
                    return CreateBackoffResult();
                }
                else
                {
                    throw;
                }
            }

            if (batch == null)
            {
                return CreateBackoffResult();
            }

            bool foundMessage = false;

            foreach (IStorageQueueMessage message in batch)
            {
                if (message == null)
                {
                    continue;
                }

                foundMessage = true;

                // Note: Capturing the cancellationToken passed here on a task that continues to run is a slight abuse
                // of the cancellation token contract. However, the timer implementation would not dispose of the
                // cancellation token source until it has stopped and perhaps also disposed, and we wait for all
                // outstanding tasks to complete before stopping the timer.
                Task task = ProcessMessageAsync(message, visibilityTimeout, cancellationToken);

                // Having both WaitForNewBatchThreshold and this method mutate _processing is safe because the timer
                // contract is serial: it only calls ExecuteAsync once the wait expires (and the wait won't expire until
                // WaitForNewBatchThreshold has finished mutating _processing).
                _processing.Add(task);
            }

            // Back off when no message was found.
            if (!foundMessage)
            {
                return CreateBackoffResult();
            }

            _foundMessageSinceLastDelay = true;
            return CreateSucceededResult();
        }

        public void Notify()
        {
            lock (_stopWaitingTaskSourceLock)
            {
                if (_stopWaitingTaskSource != null)
                {
                    _stopWaitingTaskSource.TrySetResult(null);
                }
            }
        }

        private Task CreateDelayWithNotificationTask()
        {
            Task normalDelay = Task.Delay(_delayStrategy.GetNextDelay(executionSucceeded: _foundMessageSinceLastDelay));
            _foundMessageSinceLastDelay = false;
            return Task.WhenAny(_stopWaitingTaskSource.Task, normalDelay);
        }

        private TaskSeriesCommandResult CreateBackoffResult()
        {
            return new TaskSeriesCommandResult(wait: CreateDelayWithNotificationTask());
        }

        private TaskSeriesCommandResult CreateSucceededResult()
        {
            Task wait = WaitForNewBatchThreshold();
            return new TaskSeriesCommandResult(wait);
        }

        private async Task WaitForNewBatchThreshold()
        {
            while (_processing.Count > _newBatchThreshold)
            {
                Task processed = await Task.WhenAny(_processing);
                _processing.Remove(processed);
            }
        }

        private async Task ProcessMessageAsync(IStorageQueueMessage message, TimeSpan visibilityTimeout,
            CancellationToken cancellationToken)
        {
            try
            {
                bool succeeded;

                using (ITaskSeriesTimer timer = CreateUpdateMessageVisibilityTimer(_queue, message, visibilityTimeout,
                    _backgroundExceptionDispatcher))
                {
                    timer.Start();

                    succeeded = await _triggerExecutor.ExecuteAsync(message, cancellationToken);

                    await timer.StopAsync(cancellationToken);
                }

                // Need to call Delete message only if function succeeded.
                if (succeeded)
                {
                    await DeleteMessageAsync(message, cancellationToken);
                }
                else if (_poisonQueue != null)
                {
                    if (message.DequeueCount >= _maxDequeueCount)
                    {
                        Console.WriteLine(
                            "Message has reached MaxDequeueCount of {0}. Moving message to queue '{1}'.",
                            _maxDequeueCount,
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
            catch (OperationCanceledException)
            {
                // Don't fail the top-level task when an inner task cancels.
            }
            catch (Exception exception)
            {
                // Immediately report any unhandled exception from this background task.
                // (Don't capture the exception as a fault of this Task; that would delay any exception reporting until
                // Stop is called, which might never happen.)
                _backgroundExceptionDispatcher.Throw(ExceptionDispatchInfo.Capture(exception));
            }
        }

        private static ITaskSeriesTimer CreateUpdateMessageVisibilityTimer(IStorageQueue queue,
            IStorageQueueMessage message, TimeSpan visibilityTimeout,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            // Update a message's visibility when it is halfway to expiring.
            TimeSpan normalUpdateInterval = new TimeSpan(visibilityTimeout.Ticks / 2);

            IDelayStrategy speedupStrategy = new LinearSpeedupStrategy(normalUpdateInterval, TimeSpan.FromMinutes(1));
            ITaskSeriesCommand command = new UpdateQueueMessageVisibilityCommand(queue, message, visibilityTimeout,
                speedupStrategy);
            return new TaskSeriesTimer(command, backgroundExceptionDispatcher, Task.Delay(normalUpdateInterval));
        }

        private async Task DeleteMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
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

        private async Task ReleaseMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
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

        private async Task CopyToPoisonQueueAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
        {
            await _poisonQueue.AddMessageAndCreateIfNotExistsAsync(message, cancellationToken);

            if (_sharedWatcher != null)
            {
                _sharedWatcher.Notify(_poisonQueue.Name);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
