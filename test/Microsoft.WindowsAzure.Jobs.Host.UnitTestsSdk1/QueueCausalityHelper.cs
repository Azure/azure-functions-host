using System;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    public class QueueCausalityHelperTests
    {
        // Internal and external queuing are important for interoping between simpleBatch (on the cloud)
        // and client code. 
        [Fact]
        public void ExternalProducerInternalConsumer()
        {
            // Queue outside of SimpleBatch, consume from within SimpleBatch
            string val = "abc"; // not even valid JSON. 
            CloudQueueMessage msg = new CloudQueueMessage(val);

            var qcm = new QueueCausalityHelper();
            Guid g = qcm.GetOwner(msg);
            Assert.Equal(Guid.Empty, g);

            string payload = msg.AsString;
            Assert.Equal(val, payload);
        }

        [Fact]
        public void InternalProducerExternalConsumer()
        {
            // Queue from inside of SimpleBatch, consume outside of SimpleBatch
            var qcm = new QueueCausalityHelper();
            var msg = qcm.EncodePayload(Guid.Empty, new Payload { Val = 123 });

            var json = msg.AsString;
            var obj = JsonConvert.DeserializeObject<Payload>(json);
            Assert.Equal(123, obj.Val);
        }

        [Fact]
        public void InternalProducerInternalConsumer()
        {
            // Test that we can 
            var qcm = new QueueCausalityHelper();
            Guid g = Guid.NewGuid();
            var msg = qcm.EncodePayload(g, new Payload { Val = 123 });

            var payload = msg.AsString;
            var result = JsonCustom.DeserializeObject<Payload>(payload);
            Assert.Equal(result.Val, 123);

            var owner = qcm.GetOwner(msg);
            Assert.Equal(g, owner);
        }

        [Fact]
        public void GetOwner_IfMessageIsNotValidJsonObject_ReturnsEmptyGuid()
        {
            TestOwner(Guid.Empty, "non-json");
        }

        [Fact]
        public void GetOwner_IfMessageDoesNotHaveOwnerProperty_ReturnsEmptyGuid()
        {
            TestOwner(Guid.Empty, "{'nonparent':null}");
        }

        [Fact]
        public void GetOwner_IfMessageOwnerIsNotString_ReturnsEmptyGuid()
        {
            TestOwner(Guid.Empty, "{'$AzureJobsParentId':null}");
        }

        [Fact]
        public void GetOwner_IfMessageOwnerIsNotGuid_ReturnsEmptyGuid()
        {
            TestOwner(Guid.Empty, "{'$AzureJobsParentId':'abc'}");
        }

        [Fact]
        public void GetOwner_IfMessageOwnerIsGuid_ReturnsThatGuid()
        {
            Guid expected = Guid.NewGuid();
            JObject json = new JObject();
            json.Add("$AzureJobsParentId", new JValue(expected.ToString()));

            TestOwner(expected, json.ToString());
        }

        private static void TestOwner(Guid expectedOwner, string message)
        {
            // Arrange
            QueueCausalityHelper product = new QueueCausalityHelper();
            CloudQueueMessage queueMessage = new CloudQueueMessage(message);

            // Act
            Guid owner = product.GetOwner(queueMessage);

            // Assert
            Assert.Equal(expectedOwner, owner);
        }

        public class Payload
        {
            public int Val { get; set; }
        }
    }
}
