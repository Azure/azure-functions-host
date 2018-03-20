using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.EventHubs
{
    public class EventHubSamplesEndToEndTests : IClassFixture<EventHubSamplesEndToEndTests.TestFixture>
    {
        private TestFixture _fixture;

        public EventHubSamplesEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task EventHubTrigger()
        {
            // write 3 events
            List<EventData> events = new List<EventData>();
            string[] ids = new string[3];
            for (int i = 0; i < 3; i++)
            {
                ids[i] = Guid.NewGuid().ToString();
                JObject jo = new JObject
                {
                    { "value", ids[i] }
                };
                var evt = new EventData(Encoding.UTF8.GetBytes(jo.ToString(Formatting.None)));
                evt.Properties.Add("TestIndex", i);
                events.Add(evt);
            }

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsEventHubSender");
            EventHubsConnectionStringBuilder builder = new EventHubsConnectionStringBuilder(connectionString);

            if (string.IsNullOrWhiteSpace(builder.EntityPath))
            {
                string eventHubPath = ScriptSettingsManager.Instance.GetSetting("AzureWebJobsEventHubPath");
                builder.EntityPath = eventHubPath;
            }

            EventHubClient eventHubClient = EventHubClient.CreateFromConnectionString(builder.ToString());

            await eventHubClient.SendAsync(events);

            string logs = null;
            await TestHelpers.Await(() =>
            {
                // wait until all of the 3 of the unique IDs sent
                // above have been processed
                logs = _fixture.Host.GetLog();
                return ids.All(p => logs.Contains(p));
            });

            Assert.True(logs.Contains("IsArray true"));
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() :
                base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples", "Microsoft.Azure.WebJobs.Extensions.EventHubs", "3.0.0-beta4-11268")
            {
            }

            protected override IEnumerable<string> GetActiveFunctions() => new[] { "EventHubTrigger" };
        }
    }
}
