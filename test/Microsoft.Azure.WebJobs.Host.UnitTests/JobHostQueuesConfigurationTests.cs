// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class JobHostQueuesConfigurationTests
    {
        [Fact]
        public void Constructor_Defaults()
        {
            // Arrange
            JobHostQueuesConfiguration config = new JobHostQueuesConfiguration();

            // Act & Assert
            Assert.Equal(16, config.BatchSize);
            Assert.Equal(8, config.NewBatchThreshold);
            Assert.Equal(typeof(DefaultQueueProcessorFactory), config.QueueProcessorFactory.GetType());
            Assert.Equal(QueuePollingIntervals.DefaultMaximum, config.MaxPollingInterval);
        }

        [Fact]
        public void NewBatchThreshold_CanSetAndGetValue()
        {
            // Arrange
            JobHostQueuesConfiguration config = new JobHostQueuesConfiguration();

            // Unless explicitly set, NewBatchThreshold will be computed based
            // on the current BatchSize
            config.BatchSize = 20;
            Assert.Equal(10, config.NewBatchThreshold);
            config.BatchSize = 32;
            Assert.Equal(16, config.NewBatchThreshold);

            // Once set, the set value holds
            config.NewBatchThreshold = 1000;
            Assert.Equal(1000, config.NewBatchThreshold);
            config.BatchSize = 8;
            Assert.Equal(1000, config.NewBatchThreshold);
        }
    }
}
