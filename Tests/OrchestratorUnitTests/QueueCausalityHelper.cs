using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RunnerInterfaces;

namespace OrchestratorUnitTests
{
    [TestClass]
    public class QueueCausalityHelperTests
    {
        [TestMethod]
        public void TestMethod1()
        {
            var qcm = new QueueCausalityHelper();
            Guid g = Guid.NewGuid();
            var msg = qcm.EncodePayload(g, new Payload { Val = 123 });

            var payload = qcm.DecodePayload(msg);
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
