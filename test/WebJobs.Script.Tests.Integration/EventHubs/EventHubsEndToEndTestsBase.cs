﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.EventHubs
{
    public class EventHubsEndToEndTestsBase : IClassFixture<EventHubsEndToEndTestsBase.TestFixture>
    {
        private EndToEndTestFixture _fixture;

        public EventHubsEndToEndTestsBase(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task EventHub()
        {
            // Event Hub needs the following environment vars:
            // "AzureWebJobsEventHubSender" - the connection string for the send rule
            // "AzureWebJobsEventHubReceiver"  - the connection string for the receiver rule
            // "AzureWebJobsEventHubPath" - the path

            // Test both sending and receiving from an EventHub.
            // First, manually invoke a function that has an output binding to send EventDatas to an EventHub.
            //  This tests the ability to queue eventhhubs
            string testData = Guid.NewGuid().ToString();

            await _fixture.Host.BeginFunctionAsync("EventHubSender", testData);

            // Second, there's an EventHub trigger listener on the events which will write a blob.
            // Once the blob is written, we know both sender & listener are working.
            var resultBlob = _fixture.TestOutputContainer.GetBlockBlobReference(testData);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob,
                userMessageCallback: _fixture.Host.GetLog);

            var payload = JObject.Parse(result);
            Assert.Equal(testData, (string)payload["id"]);

            var bindingData = payload["bindingData"];
            int sequenceNumber = (int)bindingData["sequenceNumber"];
            var systemProperties = bindingData["systemProperties"];
            Assert.Equal(sequenceNumber, (int)systemProperties["sequenceNumber"]);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() :
                base(@"TestScripts\Node", "node", "Microsoft.Azure.WebJobs.Extensions.EventHubs", "3.0.0-beta*")
            {
            }

            public override void ConfigureJobHost(IHostBuilder builder)
            {
                builder
                    .ConfigureServices(s =>
                    {
                        s.Configure<ScriptHostOptions>(o =>
                        {
                            o.Functions = new[] 
                            {
                                "EventHubSender",
                                "EventHubTrigger"
                            };

                            // TODO DI: This should be set automatically.
                            o.MaxMessageLengthBytes = ScriptHost.DefaultMaxMessageLengthBytesDynamicSku;
                        });
                    });
            }
        }
    }
}
