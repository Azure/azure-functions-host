// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Queues
{
    public class QueueCausalityManagerTests
    {
        // Internal and external queuing are important for interoping between simpleBatch (on the cloud)
        // and client code. 
        [Fact]
        public void ExternalProducerInternalConsumer()
        {
            // Queue outside of SimpleBatch, consume from within SimpleBatch
            string val = "abc"; // not even valid JSON. 
            CloudQueueMessage msg = new CloudQueueMessage(val);

            Guid? g = QueueCausalityManager.GetOwner(msg);
            Assert.Null(g);

            string payload = msg.AsString;
            Assert.Equal(val, payload);
        }

        [Fact]
        public void InternalProducerExternalConsumer()
        {
            // Queue from inside of SimpleBatch, consume outside of SimpleBatch
            var msg = QueueCausalityManager.EncodePayload(Guid.Empty, new Payload { Val = 123 });

            var json = msg.AsString;
            var obj = JsonConvert.DeserializeObject<Payload>(json);
            Assert.Equal(123, obj.Val);
        }

        [Fact]
        public void InternalProducerInternalConsumer()
        {
            // Test that we can 
            Guid g = Guid.NewGuid();
            var msg = QueueCausalityManager.EncodePayload(g, new Payload { Val = 123 });

            var payload = msg.AsString;
            var result = JsonCustom.DeserializeObject<Payload>(payload);
            Assert.Equal(result.Val, 123);

            var owner = QueueCausalityManager.GetOwner(msg);
            Assert.Equal(g, owner);
        }

        [Fact]
        public void GetOwner_IfMessageIsNotValidJsonObject_ReturnsNull()
        {
            TestOwnerReturnsNull("non-json");
        }

        [Fact]
        public void GetOwner_IfMessageDoesNotHaveOwnerProperty_ReturnsNull()
        {
            TestOwnerReturnsNull("{'nonparent':null}");
        }

        [Fact]
        public void GetOwner_IfMessageOwnerIsNotString_ReturnsNull()
        {
            TestOwnerReturnsNull("{'$AzureWebJobsParentId':null}");
        }

        [Fact]
        public void GetOwner_IfMessageOwnerIsNotGuid_ReturnsNull()
        {
            TestOwnerReturnsNull("{'$AzureWebJobsParentId':'abc'}");
        }

        [Fact]
        public void GetOwner_IfMessageOwnerIsGuid_ReturnsThatGuid()
        {
            Guid expected = Guid.NewGuid();
            JObject json = new JObject();
            json.Add("$AzureWebJobsParentId", new JValue(expected.ToString()));

            TestOwner(expected, json.ToString());
        }

        private static void TestOwner(Guid expectedOwner, string message)
        {
            Guid? owner = GetOwner(message);

            // Assert
            Assert.Equal(expectedOwner, owner);
        }


        private static void TestOwnerReturnsNull(string message)
        {
            Guid? owner = GetOwner(message);

            // Assert
            Assert.Null(owner);
        }

        private static Guid? GetOwner(string message)
        {
            // Arrange
            CloudQueueMessage queueMessage = new CloudQueueMessage(message);

            // Act
            return QueueCausalityManager.GetOwner(queueMessage);
        }

        public class Payload
        {
            public int Val { get; set; }
        }
    }
}
