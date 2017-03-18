using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class EventHubTests
    {
        [Fact]
        public void GetStaticBindingContract_ReturnsExpectedValue()
        {
            var strategy = new EventHubTriggerBindingStrategy();
            var contract = strategy.GetStaticBindingContract();

            Assert.Equal(7, contract.Count);
            Assert.Equal(typeof(PartitionContext), contract["PartitionContext"]);
            Assert.Equal(typeof(string), contract["Offset"]);
            Assert.Equal(typeof(long), contract["SequenceNumber"]);
            Assert.Equal(typeof(DateTime), contract["EnqueuedTimeUtc"]);
            Assert.Equal(typeof(IDictionary<string, object>), contract["Properties"]);
            Assert.Equal(typeof(IDictionary<string, object>), contract["SystemProperties"]);
        }

        [Fact]
        public void GetBindingContract_SingleDispatch_ReturnsExpectedValue()
        {
            var contract = EventHubTriggerBindingStrategy.GetBindingContract(true);

            Assert.Equal(7, contract.Count);
            Assert.Equal(typeof(PartitionContext), contract["PartitionContext"]);
            Assert.Equal(typeof(string), contract["Offset"]);
            Assert.Equal(typeof(long), contract["SequenceNumber"]);
            Assert.Equal(typeof(DateTime), contract["EnqueuedTimeUtc"]);
            Assert.Equal(typeof(IDictionary<string, object>), contract["Properties"]);
            Assert.Equal(typeof(IDictionary<string, object>), contract["SystemProperties"]);
        }

        [Fact]
        public void GetBindingContract_MultipleDispatch_ReturnsExpectedValue()
        {
            var contract = EventHubTriggerBindingStrategy.GetBindingContract(false);

            // TODO: handle multiple dispatch
            // https://github.com/Azure/azure-webjobs-sdk/issues/1072
            Assert.Equal(1, contract.Count);
            //Assert.Equal(typeof(PartitionContext[]), contract["PartitionContext"]);
            //Assert.Equal(typeof(string[]), contract["Offset"]);
            //Assert.Equal(typeof(long[]), contract["SequenceNumber"]);
            //Assert.Equal(typeof(DateTime), contract["EnqueuedTimeUtc"]);
            //Assert.Equal(typeof(IDictionary<string, object>[]), contract["Properties"]);
            //Assert.Equal(typeof(IDictionary<string, object>[]), contract["SystemProperties"]);
        }

        [Fact]
        public void GetBindingData_SingleDispatch_ReturnsExpectedValue()
        {
            var evt = new EventData();
            evt.PartitionKey = "TestKey";
            var input = EventHubTriggerInput.New(evt);
            input.PartitionContext = new PartitionContext();
            var bindingData = EventHubTriggerBindingStrategy.GetBindingData(input);

            Assert.Equal(7, bindingData.Count);
            Assert.Same(input.PartitionContext, bindingData["PartitionContext"]);
            Assert.Equal(evt.PartitionKey, bindingData["PartitionKey"]);
            Assert.Equal(evt.Offset, bindingData["Offset"]);
            Assert.Equal(evt.SequenceNumber, bindingData["SequenceNumber"]);
            Assert.Equal(evt.EnqueuedTimeUtc, bindingData["EnqueuedTimeUtc"]);
            Assert.Same(evt.Properties, bindingData["Properties"]);
            Assert.Same(evt.SystemProperties, bindingData["SystemProperties"]);
        }

        [Fact]
        public void GetBindingData_MultipleDispatch_ReturnsExpectedValue()
        {
            var events = new EventData[3]
            {
                new EventData(Encoding.UTF8.GetBytes("Event 1")),
                new EventData(Encoding.UTF8.GetBytes("Event 2")),
                new EventData(Encoding.UTF8.GetBytes("Event 3"))
            };

            var input = new EventHubTriggerInput
            {
                PartitionContext = new PartitionContext(),
                Events = events
            };
            var bindingData = EventHubTriggerBindingStrategy.GetBindingData(input);

            // TODO: handle multiple dispatch
            // https://github.com/Azure/azure-webjobs-sdk/issues/1072
            Assert.Equal(1, bindingData.Count);
            Assert.Same(input.PartitionContext, bindingData["PartitionContext"]);
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
