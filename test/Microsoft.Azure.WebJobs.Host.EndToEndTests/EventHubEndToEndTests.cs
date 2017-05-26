// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#if SERVICE_BUS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
#if SERVICE_BUS
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.ServiceBus.Messaging;
#endif
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class EventHubEndToEndTests : IDisposable
    {
        private readonly JobHost _host;
        private const string TestHubName = "webjobstesthub";
        private const string TestHub2Name = "webjobstesthub2";
        private const string TestHub2Connection = "AzureWebJobsTestHubConnection2";

        public EventHubEndToEndTests()
        {
            var config = new JobHostConfiguration()
            {
                TypeLocator = new FakeTypeLocator(typeof(EventHubTestJobs))
            };
            var eventHubConfig = new EventHubConfiguration();

            string connection = Environment.GetEnvironmentVariable("AzureWebJobsTestHubConnection");
            Assert.True(!string.IsNullOrEmpty(connection), "Required test connection string is missing.");
            eventHubConfig.AddSender(TestHubName, connection);
            eventHubConfig.AddReceiver(TestHubName, connection);

            connection = Environment.GetEnvironmentVariable(TestHub2Connection);
            Assert.True(!string.IsNullOrEmpty(connection), "Required test connection string is missing.");

            config.UseEventHub(eventHubConfig);
            _host = new JobHost(config);

            EventHubTestJobs.Result = null;
        }

        [Fact]
        public async Task EventHubTriggerTest_SingleDispatch()
        {
            await _host.StartAsync();

            try
            {
                var method = typeof(EventHubTestJobs).GetMethod("SendEvent_TestHub", BindingFlags.Static | BindingFlags.Public);
                var id = Guid.NewGuid().ToString();
                EventHubTestJobs.EventId = id;
                await _host.CallAsync(method, new { input = id });

                await TestHelpers.Await(() =>
                {
                    return EventHubTestJobs.Result != null;
                });

                Assert.Equal(id, (object)EventHubTestJobs.Result);
            }
            finally
            {
                await _host.StopAsync();
            }
        }

        [Fact]
        public async Task EventHubTriggerTest_MultipleDispatch()
        {
            // send some events BEFORE starting the host, to ensure
            // the events are received in batch
            var method = typeof(EventHubTestJobs).GetMethod("SendEvents_TestHub2", BindingFlags.Static | BindingFlags.Public);
            var id = Guid.NewGuid().ToString();
            EventHubTestJobs.EventId = id;
            int numEvents = 5;
            await _host.CallAsync(method, new { numEvents = numEvents, input = id });

            try
            {
                await _host.StartAsync();

                await TestHelpers.Await(() =>
                {
                    return EventHubTestJobs.Result != null;
                });

                var eventsProcessed = (string[])EventHubTestJobs.Result;
                Assert.True(eventsProcessed.Length >= 1);
            }
            finally
            {
                await _host.StopAsync();
            }
        }

        public void Dispose()
        {
            _host?.Dispose();
        }

        public static class EventHubTestJobs
        {
            public static string EventId;
            public static object Result { get; set; }

            public static void SendEvent_TestHub(string input, [EventHub(TestHubName)] out EventData evt)
            {
                evt = new EventData(Encoding.UTF8.GetBytes(input))
                {
                    PartitionKey = "TestPartition"
                };
                evt.Properties.Add("TestProp1", "value1");
                evt.Properties.Add("TestProp2", "value2");
            }

            public static void SendEvents_TestHub2(int numEvents, string input, [EventHub(TestHub2Name, Connection = TestHub2Connection)] out EventData[] events)
            {
                events = new EventData[numEvents];
                for (int i = 0; i < numEvents; i++)
                {
                    var evt = new EventData(Encoding.UTF8.GetBytes(input));
                    evt.PartitionKey = "TestPartition";
                    evt.Properties.Add("TestIndex", i);
                    evt.Properties.Add("TestProp1", "value1");
                    evt.Properties.Add("TestProp2", "value2");
                    events[i] = evt;
                }
            }

            public static void ProcessSingleEvent([EventHubTrigger(TestHubName)] string evt, 
                string partitionKey, DateTime enqueuedTimeUtc, IDictionary<string, object> properties,
                IDictionary<string, object> systemProperties)
            {
                // filter for the ID the current test is using
                if (evt == EventId)
                {
                    Assert.Equal("TestPartition", partitionKey);
                    Assert.True((DateTime.Now - enqueuedTimeUtc).TotalSeconds < 30);

                    Assert.Equal(2, properties.Count);
                    Assert.Equal("value1", properties["TestProp1"]);
                    Assert.Equal("value2", properties["TestProp2"]);

                    Assert.Equal(8, systemProperties.Count);

                    Result = evt;
                }
            }

            public static void ProcessMultipleEvents([EventHubTrigger(TestHub2Name, Connection = TestHub2Connection)] string[] events,
                string[] partitionKeyArray, DateTime[] enqueuedTimeUtcArray, IDictionary<string, object>[] propertiesArray,
                IDictionary<string, object>[] systemPropertiesArray)
            {
                Assert.Equal(events.Length, partitionKeyArray.Length);
                Assert.Equal(events.Length, enqueuedTimeUtcArray.Length);
                Assert.Equal(events.Length, propertiesArray.Length);
                Assert.Equal(events.Length, systemPropertiesArray.Length);

                for (int i = 0; i < events.Length; i++)
                {
                    Assert.Equal("TestPartition", partitionKeyArray[i]);
                    Assert.Equal(3, propertiesArray[i].Count);
                    Assert.Equal(8, systemPropertiesArray[i].Count);
                    Assert.Equal(i, propertiesArray[i]["TestIndex"]);
                }

                // filter for the ID the current test is using
                if (events[0] == EventId)
                {
                    Result = events;
                }
            }
        }
    }
}
#endif