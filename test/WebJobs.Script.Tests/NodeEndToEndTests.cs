// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class NodeEndToEndTests : EndToEndTestsBase<NodeEndToEndTests.TestFixture>
    {
        private const string JobLogTestFileName = "joblog.txt";

        public NodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// Function "Invalid" has a binding error. This function validates that the error
        /// is cached, and the fact that all the other tests in this suite run verifies that
        /// the error did not bring down the host.
        /// </summary>
        [Fact]
        public void ErrorFunction_DoesNotBringDownHost()
        {
            // verify the cached error for the invalid function
            Assert.Equal(1, Fixture.Host.FunctionErrors.Count);
            string error = Fixture.Host.FunctionErrors["Invalid"].Single();
            Assert.Equal("'invalid' is not a valid binding direction.", error);
        }

        [Fact]
        public async Task DocumentDB()
        {
            await DocumentDBTest();
        }

        [Fact]
        public async Task NotificationHub()
        {
            await NotificationHubTest("NotificationHubOut");
        }

        [Fact]
        public async Task EasyTables()
        {
            await EasyTablesTest();
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
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", testData }
            };
            await Fixture.Host.CallAsync("EventHubSender", arguments);

            // Second, there's an EventHub trigger listener on the events which will write a blob. 
            // Once the blob is written, we know both sender & listener are working.
            var resultBlob = Fixture.TestContainer.GetBlockBlobReference(testData);
            string result = await TestHelpers.WaitForBlobAsync(resultBlob);

            var payload = JsonConvert.DeserializeObject<Payload>(result);
            Assert.Equal(testData, payload.Id);
        }

        [Fact]
        public async Task ManualTrigger()
        {
            string testData = Guid.NewGuid().ToString();
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", testData }
            };
            await Fixture.Host.CallAsync("ManualTrigger", arguments);

            // verify use of context.log to log complex objects
            TraceEvent scriptTrace = Fixture.TraceWriter.Traces.Single(p => p.Message.Contains(testData));
            Assert.Equal(TraceLevel.Verbose, scriptTrace.Level);
            JObject logEntry = JObject.Parse(scriptTrace.Message);
            Assert.Equal("Node.js manually triggered function called!", logEntry["message"]);
            Assert.Equal(testData, logEntry["input"]);
        }

        [Fact]
        public async Task HttpTrigger_Get()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Get,
            };
            request.Headers.Add("test-header", "Test Request Header");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal("Test Response Header", response.Headers.GetValues("test-header").SingleOrDefault());

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal((string)resultObject["reqBodyType"], "undefined");
            Assert.Null((string)resultObject["reqBody"]);

            // validate input headers
            JObject reqHeaders = (JObject)resultObject["reqHeaders"];
            Assert.Equal("Test Request Header", reqHeaders["test-header"]);
        }

        [Fact]
        public async Task HttpTrigger_Post_PlainText()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(testData)
            };

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal((string)resultObject["reqBodyType"], "string");
            Assert.Equal((string)resultObject["reqBody"], testData);
        }

        [Fact]
        public async Task HttpTrigger_Post_Json()
        {
            string testData = Guid.NewGuid().ToString();
            JObject testObject = new JObject
            {
                { "testData", testData }
            };
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(testObject.ToString())
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal((string)resultObject["reqBodyType"], "object");
            Assert.Equal((string)resultObject["reqBody"]["testData"], testData);
        }

        [Fact]
        public async Task WebHookTrigger_GenericJson()
        {
            string testData = Guid.NewGuid().ToString();
            JObject testObject = new JObject
            {
                { "a", testData }
            };
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/webhooktrigger?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5")),
                Method = HttpMethod.Post,
                Content = new StringContent(testObject.ToString())
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("WebHookTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(string.Format("WebHook processed successfully! {0}", testData), body);
        }

        [Fact]
        public async Task TimerTrigger()
        {
            // job is running every second, so give it a few seconds to
            // generate some output
            await TestHelpers.Await(() =>
            {
                if (File.Exists(JobLogTestFileName))
                {
                    string[] lines = File.ReadAllLines(JobLogTestFileName);
                    return lines.Length > 2;
                }
                else
                {
                    return false;
                }
            }, timeout: 10 * 1000);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node")
            {
                File.Delete(JobLogTestFileName);
            }
        }

        private class Payload
        {
            [JsonProperty(PropertyName = "id")]
            public string Id { get; set; }

            [JsonProperty(PropertyName = "prop1")]
            public string Prop1 { get; set; }
        }
    }
}
