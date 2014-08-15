// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.Jobs.Host.Queues;
using Microsoft.Azure.Jobs.Host.Queues.Listeners;

namespace Microsoft.Azure.Jobs.Host
{
    /// <summary>Represents configuration for <see cref="QueueTriggerAttribute"/>.</summary>
    public sealed class JobHostQueuesConfiguration : IQueueConfiguration
    {
        private const int DefaultMaxDequeueCount = 5;

        private TimeSpan _maxPollingInterval = QueuePollingIntervals.DefaultMaximum;
        private int _maxDequeueCount = DefaultMaxDequeueCount;

        /// <summary>Initializes a new instance of the <see cref="JobHostQueuesConfiguration"/> class.</summary>
        internal JobHostQueuesConfiguration()
        {
        }

        /// <summary>
        /// When a queue remains empty, the longest period of time to wait before checking for a message to arrive.
        /// </summary>
        public TimeSpan MaxPollingInterval
        {
            get { return _maxPollingInterval; }

            set
            {
                if (_maxPollingInterval < QueuePollingIntervals.Minimum)
                {
                    string message = String.Format(CultureInfo.CurrentCulture,
                        "MaxPollingInterval must not be less than {0}.", QueuePollingIntervals.Minimum);
                    throw new ArgumentException(message, "value");
                }

                _maxPollingInterval = value;
            }
        }

        /// <summary>
        /// The number of times to try processing a message before moving it to the poison queue (where possible).
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
