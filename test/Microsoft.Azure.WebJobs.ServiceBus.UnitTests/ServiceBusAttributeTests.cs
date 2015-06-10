// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class ServiceBusAttributeTests
    {
        [Fact]
        public void Constructor_NoAccessSpecified_SetsExpectedValues()
        {
            ServiceBusAttribute attribute = new ServiceBusAttribute("testqueue");
            Assert.Equal("testqueue", attribute.QueueOrTopicName);
            Assert.Equal(AccessRights.Manage, attribute.Access);
        }

        [Fact]
        public void Constructor_AccessSpecified_SetsExpectedValues()
        {
            ServiceBusAttribute attribute = new ServiceBusAttribute("testqueue", AccessRights.Listen);
            Assert.Equal("testqueue", attribute.QueueOrTopicName);
            Assert.Equal(AccessRights.Listen, attribute.Access);
        }
    }
}
