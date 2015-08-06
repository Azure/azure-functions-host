// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    /// <summary>
    /// Provides context input for <see cref="IQueueProcessorFactory"/>
    /// </summary>
    [CLSCompliant(false)]
    public class QueueProcessorFactoryContext
    {
        /// <summary>
        /// Constructs a new instance
        /// </summary>
        /// <param name="queue">The <see cref="CloudQueue"/> the <see cref="QueueProcessor"/> will operate on.</param>
        /// <param name="trace">The <see cref="TraceWriter"/> to write to.</param>
        /// <param name="poisonQueue">The queue to move messages to when unable to process a message after the maximum dequeue count has been exceeded. May be null.</param>
        public QueueProcessorFactoryContext(CloudQueue queue, TraceWriter trace, CloudQueue poisonQueue = null)         
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }
            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            Queue = queue;
            PoisonQueue = poisonQueue;
            Trace = trace;
        }

        /// <summary>
        /// Constructs a new instance
        /// </summary>
        /// <param name="queue">The <see cref="CloudQueue"/> the <see cref="QueueProcessor"/> will operate on.</param>
        /// <param name="trace">The <see cref="TraceWriter"/> to write to.</param>
        /// <param name="queueConfiguration">The queue configuration.</param>
        /// <param name="poisonQueue">The queue to move messages to when unable to process a message after the maximum dequeue count has been exceeded. May be null.</param>
        internal QueueProcessorFactoryContext(CloudQueue queue, TraceWriter trace, IQueueConfiguration queueConfiguration, CloudQueue poisonQueue = null)
            : this(queue, trace, poisonQueue)
        {
            BatchSize = queueConfiguration.BatchSize;
            MaxDequeueCount = queueConfiguration.MaxDequeueCount;
            NewBatchThreshold = queueConfiguration.NewBatchThreshold;
        }

        /// <summary>
        /// Gets or sets the <see cref="CloudQueue"/> the 
        /// <see cref="QueueProcessor"/> will operate on.
        /// </summary>
        public CloudQueue Queue { get; private set; }

        /// <summary>
        /// Gets or sets the <see cref="CloudQueue"/> for
        /// poison messages that the <see cref="QueueProcessor"/> will use.
        /// May be null.
        /// </summary>
        public CloudQueue PoisonQueue { get; private set; }

        /// <summary>
        /// Gets or sets the <see cref="TraceWriter"/>.
        /// </summary>
        public TraceWriter Trace { get; private set; }

        /// <summary>
        /// Gets or sets the number of queue messages to retrieve and process in parallel (per job method).
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of times to try processing a message before moving
        /// it to the poison queue (if a poison queue is configured for the queue).
        /// </summary>
        public int MaxDequeueCount { get; set; }

        /// <summary>
        /// Gets or sets the threshold at which a new batch of messages will be fetched.
        /// </summary>
        public int NewBatchThreshold { get; set; }
    }
}
