// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Queues;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class SimpleQueueConfiguration : IQueueConfiguration
    {
        private readonly int _maxDequeueCount;

        public SimpleQueueConfiguration(int maxDequeueCount)
        {
            _maxDequeueCount = maxDequeueCount;
        }

        public int BatchSize
        {
            get { return 16; }
        }

        public int NewBatchThreshold
        {
            get
            {
                return BatchSize / 2;
            }
        }

        public TimeSpan MaxPollingInterval
        {
            get { return TimeSpan.FromMinutes(10); }
        }

        public int MaxDequeueCount
        {
            get { return _maxDequeueCount; }
        }

        public IQueueProcessorFactory QueueProcessorFactory
        {
            get
            {
                return new DefaultQueueProcessorFactory();
            }
        }
    }
}
