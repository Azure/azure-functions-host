// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Queues;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal class FakeQueueConfiguration : IQueueConfiguration
    {
        public int BatchSize
        {
            get { return 2; }
        }

        public TimeSpan MaxPollingInterval
        {
            get { return TimeSpan.FromSeconds(10); }
        }

        public int MaxDequeueCount
        {
            get { return 3; }
        }
    }
}
