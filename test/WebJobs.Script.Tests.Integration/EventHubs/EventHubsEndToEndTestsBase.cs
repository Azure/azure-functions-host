// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.DependencyInjection;
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
            IDictionary<string, object> systemProperties = bindingData["systemProperties"].ToObject<Dictionary<string, object>>();
            Assert.Equal(sequenceNumber, (long)systemProperties["sequenceNumber"]);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(@"TestScripts\Node", "node", LanguageWorkerConstants.NodeLanguageWorkerName)
            {
            }

            protected override ExtensionPackageReference[] GetExtensionsToInstall()
            {
                return new ExtensionPackageReference[]
                {
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                        Version = "3.0.0"
                    }
                };
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                                "EventHubSender",
                                "EventHubTrigger"
                            };
                });
            }
        }
    }
}
