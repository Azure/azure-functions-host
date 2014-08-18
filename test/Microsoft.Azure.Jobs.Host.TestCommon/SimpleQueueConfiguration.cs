// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

        public TimeSpan MaxPollingInterval
        {
            get { return TimeSpan.FromMinutes(10); }
        }

        public int MaxDequeueCount
        {
            get { return _maxDequeueCount; }
        }
    }
}
