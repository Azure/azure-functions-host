using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.JobsUnitTests
{
    [TestClass]
    public class QueueCausalityHelperTests
    {
        // Internal and external queuing are important for interoping between simpleBatch (on the cloud)
        // and client code. 
        [TestMethod]
        public void ExternalProducerInternalConsumer()
        {
            // Queue outside of SimpleBatch, consume from within SimpleBatch
            string val = "abc"; // not even valid JSON. 
            CloudQueueMessage msg = new CloudQueueMessage(val);

            var qcm = new QueueCausalityHelper();
            Guid g = qcm.GetOwner(msg);
            Assert.AreEqual(Guid.Empty, g);

            string payload = msg.AsString;
            Assert.AreEqual(val, payload);
        }

        [TestMethod]
        public void InternalProducerExternalConsumer()
        {
            // Queue from inside of SimpleBatch, consume outside of SimpleBatch
            var qcm = new QueueCausalityHelper();
            var msg = qcm.EncodePayload(Guid.Empty, new Payload { Val = 123 });

            var json = msg.AsString;
            var obj = JsonConvert.DeserializeObject<Payload>(json);
            Assert.AreEqual(123, obj.Val);
        }

        [TestMethod]
        public void InternalProducerInternalConsumer()
        {
            // Test that we can 
            var qcm = new QueueCausalityHelper();
            Guid g = Guid.NewGuid();
            var msg = qcm.EncodePayload(g, new Payload { Val = 123 });

            var payload = msg.AsString;
            var result = JsonCustom.DeserializeObject<Payload>(payload);
            Assert.AreEqual(result.Val, 123);

            var owner = qcm.GetOwner(msg);
            Assert.AreEqual(g, owner);
        }

        public class Payload
        {
            public int Val { get; set; }
        }
    }
}
