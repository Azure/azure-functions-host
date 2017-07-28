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
using System.Web.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(NodeEndToEndTests))]
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
            inputBlob.Metadata.Add("TestMetadataKey", "TestMetadataValue");
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

            var blobMetadata = (JObject)testResult["blobMetadata"];
            Assert.Equal($"test-input-node/{name}", (string)blobMetadata["path"]);

            var metadata = (JObject)blobMetadata["metadata"];
            Assert.Equal("TestMetadataValue", (string)metadata["testMetadataKey"]);

            var properties = (JObject)blobMetadata["properties"];
            Assert.Equal("application/octet-stream", (string)properties["contentType"]);
            Assert.Equal("BlockBlob", (string)properties["blobType"]);
            Assert.Equal(5, properties["length"]);

            string invocationId = (string)testResult["invocationId"];
            Guid.Parse(invocationId);
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
            request.SetConfiguration(new HttpConfiguration());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTriggerByteArray", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
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
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
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

            var payload = JObject.Parse(result);
            Assert.Equal(testData, (string)payload["id"]);

            var bindingData = payload["bindingData"];
            int sequenceNumber = (int)bindingData["sequenceNumber"];
            var systemProperties = bindingData["systemProperties"];
            Assert.Equal(sequenceNumber, (int)systemProperties["sequenceNumber"]);
        }

        [Fact]
        public async Task Scenario_BindingData()
        {
            JObject input = new JObject
            {
                { "scenario", "bindingData" }
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };

            // assertions are done in the script itself, so if this function succeeds
            // we're good
            await Fixture.Host.CallAsync("Scenarios", arguments);
        }

        [Fact]
        public async Task Scenario_Logging()
        {
            // Sleep to make sure all logs from other "Scenarios" tests have flushed before
            // we delete the file.
            await Task.Delay(1000);
            TestHelpers.ClearFunctionLogs("Scenarios");

            string testData = Guid.NewGuid().ToString();
            JObject input = new JObject
            {
                { "scenario", "logging" },
                { "input", testData },
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };
            await Fixture.Host.CallAsync("Scenarios", arguments);

            IList<string> logs = null;
            await TestHelpers.Await(() =>
            {
                logs = TestHelpers.GetFunctionLogsAsync("Scenarios", throwOnNoLogs: false).Result;
                return logs.Count > 0;
            });

            // verify use of context.log to log complex objects
            TraceEvent scriptTrace = Fixture.TraceWriter.Traces.Single(p => p.Message.Contains(testData));
            Assert.Equal(TraceLevel.Info, scriptTrace.Level);
            JObject logEntry = JObject.Parse(scriptTrace.Message);
            Assert.Equal("This is a test", logEntry["message"]);
            Assert.Equal("v6.5.0", (string)logEntry["version"]);
            Assert.Equal(testData, logEntry["input"]);

            // verify log levels in traces
            TraceEvent[] traces = Fixture.TraceWriter.Traces.Where(t => t.Message.Contains("loglevel")).ToArray();
            Assert.Equal(TraceLevel.Info, traces[0].Level);
            Assert.Equal("loglevel default", traces[0].Message);
            Assert.Equal(TraceLevel.Info, traces[1].Level);
            Assert.Equal("loglevel info", traces[1].Message);
            Assert.Equal(TraceLevel.Verbose, traces[2].Level);
            Assert.Equal("loglevel verbose", traces[2].Message);
            Assert.Equal(TraceLevel.Warning, traces[3].Level);
            Assert.Equal("loglevel warn", traces[3].Message);
            Assert.Equal(TraceLevel.Error, traces[4].Level);
            Assert.Equal("loglevel error", traces[4].Message);

            // verify logs made it to file logs
            Assert.True(logs.Count == 15, string.Join(Environment.NewLine, logs));

            // verify most of the logs look correct
            Assert.EndsWith("Mathew Charles", logs[5]);
            Assert.EndsWith("null", logs[6]);
            Assert.EndsWith("1234", logs[7]);
            Assert.EndsWith("true", logs[8]);
            Assert.EndsWith("loglevel default", logs[9]);
            Assert.EndsWith("loglevel info", logs[10]);
            Assert.EndsWith("loglevel verbose", logs[11]);
            Assert.EndsWith("loglevel warn", logs[12]);
            Assert.EndsWith("loglevel error", logs[13]);
        }

        private async Task<CloudBlobContainer> GetEmptyContainer(string containerName)
        {
            var container = Fixture.BlobClient.GetContainerReference(containerName);
            if (container.Exists())
            {
                foreach (CloudBlockBlob blob in container.ListBlobs())
                {
                    await blob.DeleteAsync();
                }
            }
            return container;
        }

        [Fact]
        public async Task Scenario_RandGuidBinding_GeneratesRandomIDs()
        {
            var container = await GetEmptyContainer("scenarios-output");

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

            var blobs = container.ListBlobs().Cast<CloudBlockBlob>().ToArray();
            Assert.Equal(3, blobs.Length);
            foreach (var blob in blobs)
            {
                byte[] contents = new byte[4];
                await blob.DownloadToByteArrayAsync(contents, 0);
                int blobInt = BitConverter.ToInt32(contents, 0);
                Assert.True(blobInt >= 0 && blobInt <= 3);
            }
        }

        [Fact]
        public async Task Scenario_OutputBindingContainsFunctions()
        {
            var container = await GetEmptyContainer("scenarios-output");

            JObject input = new JObject
                {
                    { "scenario", "bindingContainsFunctions" },
                    { "container", "scenarios-output" },
                };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };
            await Fixture.Host.CallAsync("Scenarios", arguments);

            var blobs = container.ListBlobs().Cast<CloudBlockBlob>().ToArray();
            Assert.Equal(1, blobs.Length);

            var blobString = await blobs[0].DownloadTextAsync();
            Assert.Equal("{\"nested\":{},\"array\":[{}],\"value\":\"value\"}", blobString);
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

        [Theory]
        [InlineData("httptrigger")]
        [InlineData("httptriggershared")]
        public async Task HttpTrigger_Get(string functionName)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/{functionName}?name=Mathew%20Charles&location=Seattle"),
                Method = HttpMethod.Get,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Add("test-header", "Test Request Header");
            string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.71 Safari/537.36";
            request.Headers.Add("user-agent", userAgent);
            string accept = "text/html, application/xhtml+xml, application/xml; q=0.9, */*; q=0.8";
            request.Headers.Add("accept", accept);
            string customHeader = "foo,bar,baz";
            request.Headers.Add("custom-1", customHeader);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal("Test Response Header", response.Headers.GetValues("test-header").SingleOrDefault());
            Assert.Equal(MediaTypeHeaderValue.Parse("application/json; charset=utf-8"), response.Content.Headers.ContentType);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("undefined", (string)resultObject["reqBodyType"]);
            Assert.Null((string)resultObject["reqBody"]);
            Assert.Equal("undefined", (string)resultObject["reqRawBodyType"]);
            Assert.Null((string)resultObject["reqRawBody"]);

            // verify binding data was populated from query parameters
            Assert.Equal("Mathew Charles", (string)resultObject["bindingData"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["bindingData"]["location"]);

            // validate input headers
            JObject reqHeaders = (JObject)resultObject["reqHeaders"];
            Assert.Equal("Test Request Header", reqHeaders["test-header"]);
            Assert.Equal(userAgent, reqHeaders["user-agent"]);
            Assert.Equal(accept, reqHeaders["accept"]);
            Assert.Equal(customHeader, reqHeaders["custom-1"]);
        }

        [Fact]
        public async Task HttpTriggerToBlob()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/HttpTriggerToBlob?suffix=TestSuffix"),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Add("Prefix", "TestPrefix");
            request.Headers.Add("value", "TestValue");

            var id = Guid.NewGuid().ToString();
            var metadata = new JObject()
            {
                { "m1", "AAA" },
                { "m2", "BBB" }
            };
            var input = new JObject()
            {
                { "id", id },
                { "value", "TestInput" },
                { "metadata", metadata }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTriggerToBlob", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            string expectedValue = $"TestInput{id}TestValue";
            Assert.Equal(expectedValue, body);

            // verify blob was written
            string blobName = $"TestPrefix-{id}-TestSuffix-BBB";
            var outBlob = Fixture.TestOutputContainer.GetBlockBlobReference(blobName);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outBlob);
            Assert.Equal(expectedValue, result);
        }

        [Theory]
        [InlineData("application/json", "{\"name\": \"test\" }", "rawresponse")]
        [InlineData("application/json", 1, "rawresponse")]
        [InlineData("application/xml", "<root>XML payload</string>", "rawresponse")]
        [InlineData("text/plain", "plain text input", "rawresponse")]
        [InlineData("text/plain", "{\"name\": \"test\" }", "rawresponsenocontenttype")]
        [InlineData("text/plain", "{\"name\": 1 }", "rawresponsenocontenttype")]
        [InlineData("text/plain", "<root>XML payload</string>", "rawresponsenocontenttype")]
        [InlineData("text/plain", "plain text input", "rawresponsenocontenttype")]
        public async Task HttpTrigger_WithRawResponse_ReturnsContent(string expectedContentType, object body, string scenario)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Get,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);

            JObject input = new JObject()
            {
                { "scenario", scenario },
                { "value", JToken.FromObject(body) },
                { "contenttype", expectedContentType }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedContentType, response.Content.Headers.ContentType.MediaType);

            object responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal(body.ToString(), responseBody);
        }

        [Fact]
        public async Task HttpTrigger_GetPlainText_WithLongResponse_ReturnsExpectedResult()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Get,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            JObject value = new JObject()
            {
                { "status", "200" },
                { "body", new string('.', 2000) }
            };
            JObject input = new JObject()
            {
                { "scenario", "echo" },
                { "value", value }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(2000, body.Length);
        }

        [Theory]
        [InlineData("application/json", "\"testinput\"")]
        [InlineData("application/xml", "<string xmlns=\"http://schemas.microsoft.com/2003/10/Serialization/\">testinput</string>")]
        [InlineData("text/plain", "testinput")]
        public async Task HttpTrigger_GetWithAccept_NegotiatesContent(string accept, string expectedBody)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Get,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

            JObject value = new JObject()
            {
                { "status", "200" },
                { "body", "testinput" }
            };
            JObject input = new JObject()
            {
                { "scenario", "echo" },
                { "value", value }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(accept, response.Content.Headers.ContentType.MediaType);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBody, body);
        }

        [Fact]
        public async Task HttpTriggerExpressApi_Get()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger?name=Mathew%20Charles&location=Seattle")),
                Method = HttpMethod.Get,
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Headers.Add("test-header", "Test Request Header");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTriggerExpressApi", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal("Test Response Header", response.Headers.GetValues("test-header").SingleOrDefault());
            Assert.Equal(MediaTypeHeaderValue.Parse("application/json; charset=utf-8"), response.Content.Headers.ContentType);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("undefined", (string)resultObject["reqBodyType"]);
            Assert.Null((string)resultObject["reqBody"]);
            Assert.Equal("undefined", (string)resultObject["reqRawBodyType"]);
            Assert.Null((string)resultObject["reqRawBody"]);

            // verify binding data was populated from query parameters
            Assert.Equal("Mathew Charles", (string)resultObject["bindingData"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["bindingData"]["location"]);

            // validate input headers
            JObject reqHeaders = (JObject)resultObject["reqHeaders"];
            Assert.Equal("Test Request Header", reqHeaders["test-header"]);
        }

        [Fact]
        public async Task HttpTriggerExpressApi_SendStatus()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Get
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Headers.Add("scenario", "sendStatus");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTriggerExpressApi", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task HttpTriggerPromise_TestBinding()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptriggerpromise")),
                Method = HttpMethod.Get,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTriggerPromise", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("returned from promise", body);
        }

        [Fact]
        public async Task HttpTrigger_Scenarios_ResBinding()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Headers.Add("scenario", "resbinding");
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("test", await response.Content.ReadAsAsync<string>());
        }

        [Fact]
        public async Task HttpTrigger_Scenarios_NullBody()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Headers.Add("scenario", "nullbody");
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Null(response.Content);
        }

        [Fact]
        public async Task HttpTrigger_Scenarios_ScalarReturn_InBody()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(new HttpConfiguration());

            JObject value = new JObject()
            {
                { "status", "200" },
                { "body", 123 }
            };
            JObject input = new JObject()
            {
                { "scenario", "echo" },
                { "value", value }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal(123, await response.Content.ReadAsAsync<int>());
        }

        [Fact]
        public async Task HttpTrigger_Scenarios_ScalarReturn()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(new HttpConfiguration());

            JObject input = new JObject()
            {
                { "scenario", "echo" },
                { "value", 123 }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal(123, await response.Content.ReadAsAsync<int>());
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
            request.SetConfiguration(new HttpConfiguration());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("string", (string)resultObject["reqBodyType"]);
            Assert.Equal(testData, (string)resultObject["reqBody"]);
            Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            Assert.Equal(testData, (string)resultObject["reqRawBody"]);
            Assert.Equal("text/plain", resultObject["reqHeaders"]["content-type"]);
        }

        [Fact]
        public async Task HttpTrigger_Post_JsonObject()
        {
            JObject testObject = new JObject
            {
                { "name", "Mathew Charles" },
                { "location", "Seattle" }
            };
            string rawBody = testObject.ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger")),
                Method = HttpMethod.Post,
                Content = new StringContent(rawBody)
            };
            request.SetConfiguration(new HttpConfiguration());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("object", (string)resultObject["reqBodyType"]);
            Assert.False((bool)resultObject["reqBodyIsArray"]);

            Assert.Equal("Mathew Charles", (string)resultObject["reqBody"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["reqBody"]["location"]);

            Assert.Equal("Mathew Charles", (string)resultObject["bindingData"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["bindingData"]["location"]);

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
            request.SetConfiguration(new HttpConfiguration());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "request", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
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
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "payload", request }
            };
            await Fixture.Host.CallAsync("WebHookTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(string.Format("WebHook processed successfully! {0}", testData), body);
        }

        [Fact]
        public async Task WebHookTrigger_NoContent()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/webhooktrigger?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5")),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(Fixture.RequestConfiguration);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "payload", request }
            };
            await Fixture.Host.CallAsync("WebHookTrigger", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(string.Format("No content"), body);
        }

        [Fact]
        public async Task TimerTrigger()
        {
            var logs = (await TestHelpers.GetFunctionLogsAsync("TimerTrigger")).ToArray();

            Assert.True(logs[1].Contains("Timer function ran!"));
        }

        [Fact]
        public async Task MultipleOutputs()
        {
            string id1 = Guid.NewGuid().ToString();
            string id2 = Guid.NewGuid().ToString();
            string id3 = Guid.NewGuid().ToString();

            JObject input = new JObject
            {
                { "id1", id1 },
                { "id2", id2 },
                { "id3", id3 }
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };
            await Fixture.Host.CallAsync("MultipleOutputs", arguments);

            // verify all 3 output blobs were written
            var blob = Fixture.TestOutputContainer.GetBlockBlobReference(id1);
            await TestHelpers.WaitForBlobAsync(blob);
            string blobContent = blob.DownloadText();
            Assert.Equal("Test Blob 1", blobContent.Trim());

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id2);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = blob.DownloadText();
            Assert.Equal("Test Blob 2", blobContent.Trim());

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id3);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = blob.DownloadText();
            Assert.Equal("Test Blob 3", blobContent.Trim());
        }

        [Fact]
        public async Task MultipleInputs()
        {
            string id = Guid.NewGuid().ToString();

            JObject input = new JObject
            {
                { "id", id },
                { "rk1", "001" },
                { "rk2", "002" }
            };
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "input", input.ToString() }
            };
            await Fixture.Host.CallAsync("MultipleInputs", arguments);

            // verify the correct output blob was written
            var blob = Fixture.TestOutputContainer.GetBlockBlobReference(id);
            await TestHelpers.WaitForBlobAsync(blob);
            string blobContent = blob.DownloadText();
            Assert.Equal("Test Entity 1, Test Entity 2", Utility.RemoveUtf8ByteOrderMark(blobContent.Trim()));
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

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            if (t.IsFaulted)
            {
                throw t.Exception;
            }
        }

        [Fact]
        public async Task PromiseResolve()
        {
            JObject input = new JObject
            {
                { "scenario", "promiseResolve" }
            };

            Task t = Fixture.Host.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            if (t.IsFaulted)
            {
                throw t.Exception;
            }
        }

        [Fact]
        public async Task PromiseApi_Resolves()
        {
            JObject input = new JObject
            {
                { "scenario", "promiseApiResolves" }
            };

            Task t = Fixture.Host.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            if (t.IsFaulted)
            {
                throw t.Exception;
            }
        }

        [Fact]
        public async Task PromiseApi_Rejects()
        {
            JObject input = new JObject
            {
                { "scenario", "promiseApiRejects" }
            };

            Task t = Fixture.Host.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));
            Assert.Same(t, result);
            Assert.Equal(true, t.IsFaulted);
            Assert.Equal("reject", t.Exception.InnerException.InnerException.Message);
        }

        [Fact]
        public async Task ExecutionContext_IsProvided()
        {
            TestHelpers.ClearFunctionLogs("Scenarios");

            JObject input = new JObject
            {
                { "scenario", "functionExecutionContext" }
            };

            Task t = Fixture.Host.CallAsync("Scenarios",
                new Dictionary<string, object>()
                {
                    { "input", input.ToString() }
                });

            Task result = await Task.WhenAny(t, Task.Delay(5000));

            var logs = await TestHelpers.GetFunctionLogsAsync("Scenarios");

            Assert.Same(t, result);
            Assert.True(logs.Any(l => l.Contains("FunctionName:Scenarios")));
            Assert.True(logs.Any(l => l.Contains($"FunctionDirectory:{Path.Combine(Fixture.Host.ScriptConfig.RootScriptPath, "Scenarios")}")));
        }

        [Fact]
        public async Task HttpTrigger_Scenarios_Buffer()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.SetConfiguration(new HttpConfiguration());

            JObject input = new JObject()
            {
                { "scenario", "buffer" },
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await Fixture.Host.CallAsync("HttpTrigger-Scenarios", arguments);

            HttpResponseMessage response = (HttpResponseMessage)request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey];
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType.MediaType);
            var array = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(0, array[0]);
            Assert.Equal(1, array[1]);
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
