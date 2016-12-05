// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.Binding;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExtensionBindingTests
    {
        [Fact]
        public void GetAttributeBuilderInfo_ServiceBusTriggerAttribute_Topic()
        {
            ServiceBusTriggerAttribute attribute = new ServiceBusTriggerAttribute("myTopic", "mySubscription", Microsoft.ServiceBus.Messaging.AccessRights.Listen);
            var builderInfo = ExtensionBinding.GetAttributeBuilderInfo(attribute);

            ServiceBusTriggerAttribute result = (ServiceBusTriggerAttribute)builderInfo.Constructor.Invoke(builderInfo.ConstructorArgs);

            Assert.Equal(attribute.TopicName, result.TopicName);
            Assert.Equal(attribute.SubscriptionName, result.SubscriptionName);
            Assert.Equal(attribute.Access, result.Access);
            Assert.Null(result.QueueName);

            Assert.Equal(0, builderInfo.Properties.Count);
        }

        [Fact]
        public void GetAttributeBuilderInfo_ServiceBusTriggerAttribute_Queue()
        {
            ServiceBusTriggerAttribute attribute = new ServiceBusTriggerAttribute("myQueue", Microsoft.ServiceBus.Messaging.AccessRights.Listen);
            var builderInfo = ExtensionBinding.GetAttributeBuilderInfo(attribute);

            ServiceBusTriggerAttribute result = (ServiceBusTriggerAttribute)builderInfo.Constructor.Invoke(builderInfo.ConstructorArgs);

            Assert.Equal(attribute.QueueName, result.QueueName);
            Assert.Equal(attribute.Access, result.Access);
            Assert.Null(result.TopicName);
            Assert.Null(result.SubscriptionName);
            Assert.Equal(0, builderInfo.Properties.Count);
        }

        [Fact]
        public void GetAttributeBuilderInfo_DocumentDBAttribute()
        {
            DocumentDBAttribute attribute = new DocumentDBAttribute("myDb", "myCollection")
            {
                CreateIfNotExists = true,
                ConnectionStringSetting = "myConnection",
                PartitionKey = "myPartition",
                Id = "myId",
                CollectionThroughput = 123
            };
            var builderInfo = ExtensionBinding.GetAttributeBuilderInfo(attribute);

            DocumentDBAttribute result = (DocumentDBAttribute)builderInfo.Constructor.Invoke(builderInfo.ConstructorArgs);

            Assert.Equal(attribute.DatabaseName, result.DatabaseName);
            Assert.Equal(attribute.CollectionName, result.CollectionName);

            Assert.Equal(5, builderInfo.Properties.Count);

            var properties = builderInfo.Properties.ToDictionary(p => p.Key.Name, p => p.Value);
            Assert.True((bool)properties["CreateIfNotExists"]);
            Assert.Equal("myConnection", (string)properties["ConnectionStringSetting"]);
            Assert.Equal("myPartition", (string)properties["PartitionKey"]);
            Assert.Equal("myId", (string)properties["Id"]);
            Assert.Equal(123, (int)properties["CollectionThroughput"]);
        }
    }
}
