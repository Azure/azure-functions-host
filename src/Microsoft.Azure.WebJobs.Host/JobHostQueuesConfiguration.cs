// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>Represents configuration for <see cref="QueueTriggerAttribute"/>.</summary>
    public sealed class JobHostQueuesConfiguration : IQueueConfiguration
    {
        private const int DefaultMaxDequeueCount = 5;
        private const int DefaultBatchSize = 16;

        private int _batchSize = DefaultBatchSize;
        private TimeSpan _maxPollingInterval = QueuePollingIntervals.DefaultMaximum;
        private int _maxDequeueCount = DefaultMaxDequeueCount;

        /// <summary>Initializes a new instance of the <see cref="JobHostQueuesConfiguration"/> class.</summary>
        internal JobHostQueuesConfiguration()
        {
        }

        /// <summary>
        /// Gets or sets the number of queue messages to retrieve and process in parallel (per job method).
        /// </summary>
        public int BatchSize
        {
            get { return _batchSize; }

            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException("value");
                }

                _batchSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the longest period of time to wait before checking for a message to arrive when a queue remains
        /// empty.
        /// </summary>
        public TimeSpan MaxPollingInterval
        {
            get { return _maxPollingInterval; }

            set
            {
                if (value < QueuePollingIntervals.Minimum)
                {
                    string message = String.Format(CultureInfo.CurrentCulture,
                        "MaxPollingInterval must not be less than {0}.", QueuePollingIntervals.Minimum);
                    throw new ArgumentException(message, "value");
                }

                _maxPollingInterval = value;
            }
        }

        /// <summary>
        /// Gets or sets the number of times to try processing a message before moving it to the poison queue (where
        /// possible).
        /// </summary>
        /// <remarks>
        /// Some queues do not have corresponding poison queues, and this property does not apply to them. Specifically,
        /// there are no corresponding poison queues for any queue whose name already ends in "-poison" or any queue
        /// whose name is already too long to add a "-poison" suffix.
        /// </remarks>
        public int MaxDequeueCount
        {
            get { return _maxDequeueCount; }

            set
            {
                if (value < 1)
                {
                    throw new ArgumentException("MaxDequeueCount must not be less than 1.", "value");
                }

                _maxDequeueCount = value;
            }
        }
    }
}
