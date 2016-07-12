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
using Microsoft.Azure.WebJobs.Script.Tests.ApiHub;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NodeEndToEndTests : EndToEndTestsBase<NodeEndToEndTests.TestFixture>
    {
        public NodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ServiceBusQueueTriggerToBlobTest()
        {
            await ServiceBusQueueTriggerToBlobTestImpl();
        }

        [Fact]
        public async Task BlobTriggerToBlobTest()
        {
            TestHelpers.ClearFunctionLogs("BlobTriggerToBlob");

            // write a binary blob
            string name = Guid.NewGuid().ToString();
            CloudBlockBlob inputBlob = Fixture.TestInputContainer.GetBlockBlobReference(name);
            byte[] inputBytes = new byte[] { 1, 2, 3, 4, 5 };
            using (var stream = inputBlob.OpenWrite())
            {
                stream.Write(inputBytes, 0, inputBytes.Length);
            }

            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference(name);
            await TestHelpers.WaitForBlobAsync(resultBlob);

            byte[] resultBytes;
            using (var resultStream = resultBlob.OpenRead())
            using (var ms = new MemoryStream())
            {
                resultStream.CopyTo(ms);
                resultBytes = ms.ToArray();
            }

            JObject testResult = await GetFunctionTestResult("BlobTriggerToBlob");
            Assert.Equal(inputBytes, resultBytes);
            Assert.True((bool)testResult["isBuffer"]);
            Assert.Equal(5, (int)testResult["length"]);
        }

        [Fact]
        public async Task QueueTriggerByteArray()
        {
            TestHelpers.ClearFunctionLogs("QueueTriggerByteArray");

            // write a binary queue message
            byte[] inputBytes = new byte[] { 1, 2, 3 };
            CloudQueueMessage message = new CloudQueueMessage(inputBytes);
            var queue = Fixture.QueueClient.GetQueueReference("test-input-byte");
            queue.CreateIfNotExists();
            queue.Clear();
            queue.AddMessage(message);

            JObject testResult = await GetFunctionTestResult("QueueTriggerByteArray");
            Assert.True((bool)testResult["isBuffer"]);
            Assert.Equal(5, (int)testResult["length"]);
        }

        [Fact]
        public async Task HttpTrigger_Post_ByteArray()
        {
            TestHelpers.ClearFunctionLogs("HttpTriggerByteArray");

            byte[] inputBytes = new byte[] { 1, 2, 3, 4, 5 };
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptriggerbytearray")),
                Method = HttpMethod.Post,
                Content = new ByteArrayContent(inputBytes)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTriggerByteArray", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties["MS_AzureFunctionsHttpResponse"];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            JObject testResult = await GetFunctionTestResult("HttpTriggerByteArray");
            Assert.True((bool)testResult["isBuffer"]);
            Assert.Equal(5, (int)testResult["length"]);
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
        public async Task TableInput()
        {
            await TableInputTest();
        }

        [Fact]
        public async Task TableOutput()
        {
            await TableOutputTest();
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
        public async Task NotificationHubNative()
        {
            await NotificationHubTest("NotificationHubNative");
        }

        [Fact]
        public async Task MobileTables()
        {
            await MobileTablesTest();
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
            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference(testData);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(resultBlob);

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
            Assert.Equal(TraceLevel.Info, scriptTrace.Level);
            JObject logEntry = JObject.Parse(scriptTrace.Message);
            Assert.Equal("Node.js manually triggered function called!", logEntry["message"]);
            Assert.Equal(testData, logEntry["input"]);
        }

        [Fact]
        public async Task Scenario_DoneCalledMultipleTimes_ErrorIsLogged()
        {
            TestHelpers.ClearFunctionLogs("Scenarios");

            JObject input = new JObject
            {
                { "scenario", "doubleDone" }
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };
            await Fixture.Host.CallAsync("Scenarios", arguments);

            var logs = await TestHelpers.GetFunctionLogsAsync("Scenarios");

            Assert.Equal(4, logs.Count);
            Assert.True(logs.Any(p => p.Contains("Function started")));
            Assert.True(logs.Any(p => p.Contains("Running scenario 'doubleDone'")));

            // verify an error was written
            Assert.True(logs.Any(p => p.Contains("Error: 'done' has already been called. Please check your script for extraneous calls to 'done'.")));

            // verify the function completed successfully
            Assert.True(logs.Any(p => p.Contains("Function completed (Success")));
        }

        [Fact]
        public async Task Scenario_RandGuidBinding_GeneratesRandomIDs()
        {
            var container = Fixture.BlobClient.GetContainerReference("scenarios-output");
            if (container.Exists())
            {
                foreach (CloudBlockBlob blob in container.ListBlobs())
                {
                    await blob.DeleteAsync();
                }
            }

            // Call 3 times - expect 3 separate output blobs
            for (int i = 0; i < 3; i++)
            {
                JObject input = new JObject
                {
                    { "scenario", "randGuid" },
                    { "container", "scenarios-output" },
                    { "value", i }
                };
                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "input", input.ToString() }
                };
                await Fixture.Host.CallAsync("Scenarios", arguments);
            }

            var blobs = container.ListBlobs().Cast<CloudBlockBlob>().OrderBy(p => p.Properties.LastModified).ToArray();
            Assert.Equal(3, blobs.Length);
            for (int i = 0; i < 3; i++)
            {
                var blob = (CloudBlockBlob)blobs[i];
                byte[] contents = new byte[4];
                await blob.DownloadToByteArrayAsync(contents, 0);
                Assert.Equal(i, BitConverter.ToInt32(contents, 0));
            }
        }

        [Fact]
        public async Task MultipleExports()
        {
            TestHelpers.ClearFunctionLogs("MultipleExports");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", string.Empty }
            };
            await Fixture.Host.CallAsync("MultipleExports", arguments);

            var logs = await TestHelpers.GetFunctionLogsAsync("MultipleExports");

            Assert.Equal(3, logs.Count);
            Assert.True(logs[1].Contains("Exports: IsObject=true, Count=4"));
        }

        [Fact]
        public async Task SingleNamedExport()
        {
            TestHelpers.ClearFunctionLogs("SingleNamedExport");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", string.Empty }
            };
            await Fixture.Host.CallAsync("SingleNamedExport", arguments);

            var logs = await TestHelpers.GetFunctionLogsAsync("SingleNamedExport");

            Assert.Equal(3, logs.Count);
            Assert.True(logs[1].Contains("Exports: IsObject=true, Count=1"));
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
            Assert.Equal("undefined", (string)resultObject["reqBodyType"]);
            Assert.Null((string)resultObject["reqBody"]);
            Assert.Equal("undefined", (string)resultObject["reqRawBodyType"]);
            Assert.Null((string)resultObject["reqRawBody"]);

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
            Assert.Equal("string", (string)resultObject["reqBodyType"]);
            Assert.Equal(testData, (string)resultObject["reqBody"]);
            Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            Assert.Equal(testData, (string)resultObject["reqRawBody"]);
        }

        [Fact]
        public async Task HttpTrigger_Post_JsonObject()
        {
            string testData = Guid.NewGuid().ToString();
            JObject testObject = new JObject
            {
                { "testData", testData }
            };
            string rawBody = testObject.ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(rawBody)
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
            Assert.Equal("object", (string)resultObject["reqBodyType"]);
            Assert.False((bool)resultObject["reqBodyIsArray"]);
            Assert.Equal(testData, (string)resultObject["reqBody"]["testData"]);
            Assert.Equal(testData, (string)resultObject["bindingData"]["testData"]);
            Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            Assert.Equal(rawBody, (string)resultObject["reqRawBody"]);
        }

        [Fact]
        public async Task HttpTrigger_Post_JsonArray()
        {
            string testData = Guid.NewGuid().ToString();
            JArray subArray = new JArray()
            {
                new JObject()
                {
                    { "type", "Dog" },
                    { "name", "Ruby" }
                },
                new JObject()
                {
                    { "type", "Cat" },
                    { "name", "Roscoe" }
                }
            };
            JArray testArray = new JArray()
            {
                new JObject()
                {
                    { "id", 1 },
                    { "name", "Larry" }
                },
                new JObject()
                {
                    { "id", 2 },
                    { "name", "Moe" }
                },
                new JObject()
                {
                    { "id", 3 },
                    { "name", "Curly" },
                    { "pets", subArray }
                }
            };

            string rawBody = testArray.ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(rawBody)
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
            Assert.Equal("object", (string)resultObject["reqBodyType"]);
            Assert.True((bool)resultObject["reqBodyIsArray"]);
            JArray resultArray = (JArray)resultObject["reqBody"];
            Assert.Equal(3, resultArray.Count);

            JObject item = (JObject)resultArray[2];
            Assert.Equal("Curly", (string)item["name"]);
            resultArray = (JArray)item["pets"];
            Assert.Equal(2, resultArray.Count);

            Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            Assert.Equal(rawBody, (string)resultObject["reqRawBody"]);
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
                { "payload", request }
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
            var logs = (await TestHelpers.GetFunctionLogsAsync("TimerTrigger")).ToArray();

            Assert.Equal(3, logs.Length);
            Assert.True(logs[1].Contains("Timer function ran!"));
        }

        [Fact]
        public async Task ApiHubTableEntityIn()
        {
            TestHelpers.ClearFunctionLogs("ApiHubTableEntityIn");

            // Ensure the test entity exists.
            await ApiHubTestHelper.EnsureEntityAsync(ApiHubTestHelper.EntityId4);

            // Test table entity out binding.
            JObject input = new JObject
            {
                { "table", "SampleTable" },
                { "id", ApiHubTestHelper.EntityId4 }
            };
            await Fixture.Host.CallAsync("ApiHubTableEntityIn",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            var logs = await TestHelpers.GetFunctionLogsAsync("ApiHubTableEntityIn");
            string expectedLog = string.Format("TestResult: {0}", ApiHubTestHelper.EntityId4);
            Assert.True(logs.Any(p => p.Contains(expectedLog)));
        }

        [Fact]
        public async Task ApiHubTableEntityOut()
        {
            var textArgValue = ApiHubTestHelper.NewRandomString();

            // Delete the test entity if it exists.
            await ApiHubTestHelper.DeleteEntityAsync(ApiHubTestHelper.EntityId5);

            // Test table entity out binding.
            JObject input = new JObject
            {
                { "table", "SampleTable" },
                { "value", textArgValue }
            };
            await Fixture.Host.CallAsync("ApiHubTableEntityOut",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            await ApiHubTestHelper.AssertTextUpdatedAsync(
                textArgValue, ApiHubTestHelper.EntityId5);
        }

        [Fact]
        public void ExcludedFunction_NotAddedToHost()
        {
            // Make sure the function was not registered
            var function = Fixture.Host.Functions.SingleOrDefault(p => string.Compare(p.Name, "Excluded") == 0);
            Assert.Null(function);

            // Make sure the host log was written
            var trace = Fixture.TraceWriter.Traces.SingleOrDefault(p => p.Message == "Function 'Excluded' is marked as excluded");
            Assert.NotNull(trace);
            Assert.Equal(TraceLevel.Info, trace.Level);
        }

        [Fact]
        public async Task NextTick()
        {
            // See https://github.com/tjanczuk/edge/issues/325.
            // This ensures the workaround is working

            // we're not going to await this call as it may hang if there is 
            // a regression, instead, monitor for IsCompleted below.
            JObject input = new JObject
            {
                { "scenario", "nextTick" }
            };
            Task t = Fixture.Host.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            await TestHelpers.Await(() =>
            {
                return t.IsCompleted;
            }, timeout: 5000, pollingInterval: 1000);

            // Await the task to force any exception to be thrown
            await t;
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node", "node")
            {
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
