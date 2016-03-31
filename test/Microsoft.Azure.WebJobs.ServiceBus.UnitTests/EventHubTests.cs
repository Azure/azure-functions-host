using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class EventHubTests
    {
        [Fact]
        public void StrategyStaticContract()
        {
            var strategy = new EventHubTriggerBindingStrategy();
            var contract = strategy.GetStaticBindingContract();

            Assert.Equal(1, contract.Count);
            Assert.Equal(typeof(PartitionContext), contract["partitionContext"]);
        }

        [Fact]
        public void TriggerStrategy()
        {
            string data = "123";

            var strategy = new EventHubTriggerBindingStrategy();
            EventHubTriggerInput triggerInput = strategy.ConvertFromString(data);

            var contract = strategy.GetContractInstance(triggerInput);

            EventData single = strategy.BindSingle(triggerInput, null);
            string body = Encoding.UTF8.GetString(single.GetBytes());

            Assert.Equal(data, body);
            Assert.Null(contract["PartitionContext"]);
            Assert.Null(contract["partitioncontext"]); // case insensitive
        }
    }
}
