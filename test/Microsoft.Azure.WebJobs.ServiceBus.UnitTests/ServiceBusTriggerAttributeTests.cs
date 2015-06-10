// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class ServiceBusTriggerAttributeTests
    {
        [Fact]
        public void Constructor_Queue_SetsExpectedValues()
        {
            ServiceBusTriggerAttribute attribute = new ServiceBusTriggerAttribute("testqueue");
            Assert.Equal("testqueue", attribute.QueueName);
            Assert.Null(attribute.SubscriptionName);
            Assert.Null(attribute.TopicName);
            Assert.Equal(AccessRights.Manage, attribute.Access);

            attribute = new ServiceBusTriggerAttribute("testqueue", AccessRights.Listen);
            Assert.Equal("testqueue", attribute.QueueName);
            Assert.Null(attribute.SubscriptionName);
            Assert.Null(attribute.TopicName);
            Assert.Equal(AccessRights.Listen, attribute.Access);
        }

        [Fact]
        public void Constructor_Topic_SetsExpectedValues()
        {
            ServiceBusTriggerAttribute attribute = new ServiceBusTriggerAttribute("testtopic", "testsubscription");
            Assert.Null(attribute.QueueName);
            Assert.Equal("testtopic", attribute.TopicName);
            Assert.Equal("testsubscription", attribute.SubscriptionName);
            Assert.Equal(AccessRights.Manage, attribute.Access);

            attribute = new ServiceBusTriggerAttribute("testtopic", "testsubscription", AccessRights.Listen);
            Assert.Null(attribute.QueueName);
            Assert.Equal("testtopic", attribute.TopicName);
            Assert.Equal("testsubscription", attribute.SubscriptionName);
            Assert.Equal(AccessRights.Listen, attribute.Access);
        }
    }
}
