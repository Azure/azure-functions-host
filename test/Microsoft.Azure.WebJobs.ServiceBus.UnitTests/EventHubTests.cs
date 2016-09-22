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

        // Validate that if connection string has EntityPath, that takes precedence over the parameter. 
        [Theory]
        [InlineData("k1", "Endpoint=sb://test89123-ns-x.servicebus.windows.net/;SharedAccessKeyName=ReceiveRule;SharedAccessKey=secretkey")]
        [InlineData("path2", "Endpoint=sb://test89123-ns-x.servicebus.windows.net/;SharedAccessKeyName=ReceiveRule;SharedAccessKey=secretkey;EntityPath=path2")]
        public void EntityPathInConnectionString(string expectedPathName, string connectionString)
        {
            EventHubConfiguration config = new EventHubConfiguration();
            IEventHubProvider provider = config;

            // Test sender 
            config.AddSender("k1", connectionString);
            var client = config.GetEventHubClient("k1");
            Assert.Equal(expectedPathName, client.Path);
        }


        [Theory]
        [InlineData("e", "n1", "n1/e/")]
        [InlineData("e--1", "host_.path.foo", "host_.path.foo/e--1/")]
        [InlineData("Ab", "Cd", "cd/ab/")]
        [InlineData("A=", "Cd", "cd/a:3D/")]
        [InlineData("A:", "Cd", "cd/a:3A/")]
        public void EventHubBlobPrefix(string eventHubName, string serviceBusNamespace, string expected)
        {
            string actual = EventHubConfiguration.GetBlobPrefix(eventHubName, serviceBusNamespace);
            Assert.Equal(expected, actual);
        }
    }
}
