using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ServiceBus.Messaging;
using Xunit;
using Microsoft.Azure.WebJobs.Host.Config;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class EventHubTests
    {
        [Fact]
        public void GetStaticBindingContract_ReturnsExpectedValue()
        {
            var strategy = new EventHubTriggerBindingStrategy();
            var contract = strategy.GetBindingContract();

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
            var strategy = new EventHubTriggerBindingStrategy();
            var contract = strategy.GetBindingContract(true);

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
            var strategy = new EventHubTriggerBindingStrategy();
            var contract = strategy.GetBindingContract(false);

            Assert.Equal(7, contract.Count);
            Assert.Equal(typeof(PartitionContext), contract["PartitionContext"]);
            Assert.Equal(typeof(string[]), contract["PartitionKeyArray"]);
            Assert.Equal(typeof(string[]), contract["OffsetArray"]);
            Assert.Equal(typeof(long[]), contract["SequenceNumberArray"]);
            Assert.Equal(typeof(DateTime[]), contract["EnqueuedTimeUtcArray"]);
            Assert.Equal(typeof(IDictionary<string, object>[]), contract["PropertiesArray"]);
            Assert.Equal(typeof(IDictionary<string, object>[]), contract["SystemPropertiesArray"]);
        }

        [Fact]
        public void GetBindingData_SingleDispatch_ReturnsExpectedValue()
        {
            var evt = new EventData();
            evt.PartitionKey = "TestKey";
            var input = EventHubTriggerInput.New(evt);
            input.PartitionContext = new PartitionContext();

            var strategy = new EventHubTriggerBindingStrategy();
            var bindingData = strategy.GetBindingData(input);

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
                new EventData(Encoding.UTF8.GetBytes("Event 1"))
                {
                    PartitionKey = "pk1"
                },
                new EventData(Encoding.UTF8.GetBytes("Event 2"))
                {
                    PartitionKey = "pk2"
                },
                new EventData(Encoding.UTF8.GetBytes("Event 3"))
                {
                    PartitionKey = "pk3"
                },
            };

            var input = new EventHubTriggerInput
            {
                PartitionContext = new PartitionContext(),
                Events = events
            };
            var strategy = new EventHubTriggerBindingStrategy();
            var bindingData = strategy.GetBindingData(input);

            Assert.Equal(7, bindingData.Count);
            Assert.Same(input.PartitionContext, bindingData["PartitionContext"]);

            // verify an array was created for each binding data type
            Assert.Equal(events.Length, ((string[])bindingData["PartitionKeyArray"]).Length);
            Assert.Equal(events.Length, ((string[])bindingData["OffsetArray"]).Length);
            Assert.Equal(events.Length, ((long[])bindingData["SequenceNumberArray"]).Length);
            Assert.Equal(events.Length, ((DateTime[])bindingData["EnqueuedTimeUtcArray"]).Length);
            Assert.Equal(events.Length, ((IDictionary<string, object>[])bindingData["PropertiesArray"]).Length);
            Assert.Equal(events.Length, ((IDictionary<string, object>[])bindingData["SystemPropertiesArray"]).Length);

            // verify event values are distributed to arrays properly
            Assert.Equal(events[0].PartitionKey, ((string[])bindingData["PartitionKeyArray"])[0]);
            Assert.Equal(events[1].PartitionKey, ((string[])bindingData["PartitionKeyArray"])[1]);
            Assert.Equal(events[2].PartitionKey, ((string[])bindingData["PartitionKeyArray"])[2]);
        }

        [Fact]
        public void TriggerStrategy()
        {
            string data = "123";

            var strategy = new EventHubTriggerBindingStrategy();
            EventHubTriggerInput triggerInput = strategy.ConvertFromString(data);

            var contract = strategy.GetBindingData(triggerInput);

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

            // Test sender 
            config.AddSender("k1", connectionString);
            var client = config.GetEventHubClient("k1", null);
            Assert.Equal(expectedPathName, client.Path);
        }

        private class TestNameResolver : INameResolver
        {
            public IDictionary<string, string> env = new Dictionary<string, string>();

            public string Resolve(string name) => env[name];
        }

        // Validate that if connection string has EntityPath, that takes precedence over the parameter. 
        [Theory]
        [InlineData("k1", "Endpoint=sb://test89123-ns-x.servicebus.windows.net/;SharedAccessKeyName=ReceiveRule;SharedAccessKey=secretkey")]
        [InlineData("path2", "Endpoint=sb://test89123-ns-x.servicebus.windows.net/;SharedAccessKeyName=ReceiveRule;SharedAccessKey=secretkey;EntityPath=path2")]
        public void GetEventHubClient_AddsConnection(string expectedPathName, string connectionString)
        {
            EventHubConfiguration config = new EventHubConfiguration();
            var client = config.GetEventHubClient("k1", connectionString);
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

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(200)]
        public void EventHubBatchCheckpointFrequency(int num)
        {
            var config = new EventHubConfiguration();
            config.BatchCheckpointFrequency = num;
            Assert.Equal(num, config.BatchCheckpointFrequency);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void EventHubBatchCheckpointFrequency_Throws(int num)
        {
            var config = new EventHubConfiguration();
            Assert.Throws<InvalidOperationException>(() => config.BatchCheckpointFrequency = num);
        }

        [Fact]
        public void InitializeFromHostMetadata()
        {
            var config = new EventHubConfiguration();
            var context = new ExtensionConfigContext()
            {
                Config = new JobHostConfiguration()
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    HostConfigMetadata = new JObject
#pragma warning restore CS0618 // Type or member is obsolete
                    {
                        {
                            "EventHub", new JObject {
                                { "MaxBatchSize", 100 },
                                { "PrefetchCount", 200 },
                                { "BatchCheckpointFrequency", 5 }
                            }
                        }
                    }
                }
            };

            (config as IExtensionConfigProvider).Initialize(context);

            var options = config.GetOptions();
            Assert.Equal(options.MaxBatchSize, 100);
            Assert.Equal(options.PrefetchCount, 200);
            Assert.Equal(config.BatchCheckpointFrequency, 5);
        }
    }
}
