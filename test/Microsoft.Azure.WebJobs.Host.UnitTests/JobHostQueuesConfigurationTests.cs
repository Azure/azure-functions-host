// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class JobHostQueuesConfigurationTests
    {
        [Fact]
        public void Constructor_Defaults()
        {
            JobHostQueuesConfiguration config = new JobHostQueuesConfiguration();

            Assert.Equal(16, config.BatchSize);
            Assert.Equal(8, config.NewBatchThreshold);
            Assert.Equal(typeof(DefaultQueueProcessorFactory), config.QueueProcessorFactory.GetType());
            Assert.Equal(QueuePollingIntervals.DefaultMaximum, config.MaxPollingInterval);
        }

        [Fact]
        public void NewBatchThreshold_CanSetAndGetValue()
        {
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

        [Fact]
        public void VisibilityTimeout_CanGetAndSetValue()
        {
            JobHostQueuesConfiguration config = new JobHostQueuesConfiguration();

            Assert.Equal(TimeSpan.Zero, config.VisibilityTimeout);

            config.VisibilityTimeout = TimeSpan.FromSeconds(30);
            Assert.Equal(TimeSpan.FromSeconds(30), config.VisibilityTimeout);
        }

        [Fact]
        public void JsonSerialization()
        {
            var jo = new JObject
            {
                { "MaxPollingInterval", 5000 }
            };
            var config = jo.ToObject<JobHostQueuesConfiguration>();
            Assert.Equal(TimeSpan.FromMilliseconds(5000), config.MaxPollingInterval);
            string json = JsonConvert.SerializeObject(config);

            jo = new JObject
            {
                { "MaxPollingInterval", "00:00:05" }
            };
            config = jo.ToObject<JobHostQueuesConfiguration>();
            Assert.Equal(TimeSpan.FromMilliseconds(5000), config.MaxPollingInterval);
            json = JsonConvert.SerializeObject(config);
        }
    }
}
