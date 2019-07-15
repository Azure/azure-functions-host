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
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(NodeEndToEndTests))]
    public class NodeEndToEndTests : EndToEndTestsBase<NodeEndToEndTests.TestFixture>
    {
        public NodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task BlobTriggerToBlobTest()
        {
            // write a binary blob
            string name = Guid.NewGuid().ToString();
            CloudBlockBlob inputBlob = Fixture.TestInputContainer.GetBlockBlobReference(name);
            inputBlob.Metadata.Add("TestMetadataKey", "TestMetadataValue");
            byte[] inputBytes = new byte[] { 1, 2, 3, 4, 5 };
            using (var stream = await inputBlob.OpenWriteAsync())
            {
                stream.Write(inputBytes, 0, inputBytes.Length);
            }

            var resultBlob = Fixture.TestOutputContainer.GetBlockBlobReference(name);
            await TestHelpers.WaitForBlobAsync(resultBlob);

            byte[] resultBytes;
            using (var resultStream = await resultBlob.OpenReadAsync())
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
            Assert.Equal("BlockBlob", Enum.Parse(typeof(BlobType), (string)properties["blobType"]).ToString());
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
            CloudQueueMessage message = CloudQueueMessage.CreateCloudQueueMessageFromByteArray(inputBytes);
            var queue = Fixture.QueueClient.GetQueueReference("test-input-byte");
            await queue.CreateIfNotExistsAsync();
            await queue.ClearAsync();
            await queue.AddMessageAsync(message);

            JObject testResult = await GetFunctionTestResult("QueueTriggerByteArray");
            Assert.True((bool)testResult["isBuffer"]);
            Assert.Equal(5, (int)testResult["length"]);
        }

        /// <summary>
        /// Function "Invalid" has a binding error. This function validates that the error
        /// is cached, and the Fact that all the other tests in this suite run verifies that
        /// the error did not bring down the host.
        /// </summary>
        [Fact]
        public async Task ErrorFunction_DoesNotBringDownHost()
        {
            // verify the cached error for the invalid function
            FunctionStatus status = await Fixture.Host.GetFunctionStatusAsync("Invalid");
            string error = status.Errors.Single();
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

        [Fact(Skip = "Not yet enabled.")]
        public void NotificationHub()
        {
            // await NotificationHubTest("NotificationHubOut");
        }

        [Fact(Skip = "Not yet enabled.")]
        public void NotificationHubNative()
        {
            // await NotificationHubTest("NotificationHubNative");
        }

        [Fact(Skip = "Not yet enabled.")]
        public void MobileTables()
        {
            //await MobileTablesTest();
        }

        [Fact]
        public async Task Scenario_BindingData()
        {
            var input = new ScenarioInput
            {
                Scenario = "bindingData",
                Value = Guid.NewGuid().ToString()
            };

            await Fixture.Host.BeginFunctionAsync("Scenarios", JObject.FromObject(input));

            // Change assert to watch for a log
            await WaitForTraceAsync("Scenarios", log => log.FormattedMessage.Contains(input.Value));
        }

        [Fact]
        public async Task Scenario_Logging()
        {
            string testData = Guid.NewGuid().ToString();
            JObject input = new JObject
            {
                { "scenario", "logging" },
                { "input", testData },
            };

            Fixture.Host.ClearLogMessages();
            Fixture.MetricsLogger.ClearCollections();

            await Fixture.Host.BeginFunctionAsync("Scenarios", input);

            string userCategory = LogCategories.CreateFunctionUserCategory("Scenarios");
            IList<string> userLogs = null;
            string consoleLog = null;
            await TestHelpers.Await(() =>
            {
                userLogs = Fixture.Host.GetScriptHostLogMessages(userCategory).Select(p => p.FormattedMessage).ToList();
                consoleLog = Fixture.Host.GetScriptHostLogMessages(LanguageWorkerConstants.FunctionConsoleLogCategoryName).Select(p => p.FormattedMessage).SingleOrDefault();
                return userLogs.Count == 10 && consoleLog != null;
            }, userMessageCallback: Fixture.Host.GetLog);

            // verify use of context.log to log complex objects
            LogMessage scriptTrace = Fixture.Host.GetScriptHostLogMessages(userCategory).Single(p => p.FormattedMessage != null && p.FormattedMessage.Contains(testData));
            Assert.Equal(LogLevel.Information, scriptTrace.Level);
            JObject logEntry = JObject.Parse(scriptTrace.FormattedMessage);
            Assert.Equal("This is a test", logEntry["message"]);
            Assert.Equal(testData, logEntry["input"]);

            // verify log levels in traces
            LogMessage[] traces = Fixture.Host.GetScriptHostLogMessages(userCategory).Where(t => t.FormattedMessage != null && t.FormattedMessage.Contains("loglevel")).ToArray();
            Assert.Equal(LogLevel.Information, traces[0].Level);
            Assert.Equal("loglevel default", traces[0].FormattedMessage);
            Assert.Equal(LogLevel.Information, traces[1].Level);
            Assert.Equal("loglevel info", traces[1].FormattedMessage);
            Assert.Equal(LogLevel.Trace, traces[2].Level);
            Assert.Equal("loglevel verbose", traces[2].FormattedMessage);
            Assert.Equal(LogLevel.Warning, traces[3].Level);
            Assert.Equal("loglevel warn", traces[3].FormattedMessage);
            Assert.Equal(LogLevel.Error, traces[4].Level);
            Assert.Equal("loglevel error", traces[4].FormattedMessage);

            // verify most of the logs look correct
            Assert.EndsWith("Mathew Charles", userLogs[1]);
            Assert.EndsWith("null", userLogs[2]);
            Assert.EndsWith("1234", userLogs[3]);
            Assert.EndsWith("true", userLogs[4]);
            Assert.EndsWith("loglevel default", userLogs[5]);
            Assert.EndsWith("loglevel info", userLogs[6]);

            // verify the console log
            Assert.Equal("console log", consoleLog);

            // We only expect 9 user log metrics to be counted, since
            // verbose logs are filtered by default (the TestLogger explicitly
            // allows all levels for testing purposes)
            var key = MetricsEventManager.GetAggregateKey(MetricEventNames.FunctionUserLog, "Scenarios");
            Assert.Equal(9, Fixture.MetricsLogger.LoggedEvents.Where(p => p == key).Count());

            // Make sure that no user logs made it to the EventGenerator (which the SystemLogger writes to)
            IEnumerable<FunctionTraceEvent> allLogs = Fixture.EventGenerator.GetFunctionTraceEvents();
            Assert.False(allLogs.Any(l => l.Summary.Contains("loglevel")));
            Assert.False(allLogs.Any(l => l.Summary.Contains("after done")));
            Assert.False(allLogs.Any(l => l.Source.EndsWith(".User")));
            Assert.False(allLogs.Any(l => l.Source == LanguageWorkerConstants.FunctionConsoleLogCategoryName));
            Assert.NotEmpty(allLogs);
        }

        [Fact]
        public async Task RandGuidBinding_GeneratesRandomIDs()
        {
            var blobs = await Scenario_RandGuidBinding_GeneratesRandomIDs();

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

            await Fixture.Host.BeginFunctionAsync("Scenarios", input);

            IEnumerable<CloudBlockBlob> blobs = null;
            await TestHelpers.Await(async () =>
            {
                blobs = await TestHelpers.ListBlobsAsync(container);
                return blobs.Count() == 1;
            });

            var blobString = await blobs.Single().DownloadTextAsync();
            Assert.Equal("{\"nested\":{},\"array\":[{}],\"value\":\"value\"}", blobString);
        }

        [Fact]
        public async Task MultipleExports()
        {
            await Fixture.Host.BeginFunctionAsync("MultipleExports", new JObject());
            await WaitForTraceAsync("MultipleExports",
                log => log.FormattedMessage.Contains("Exports: IsObject=true, Count=4"));
        }

        [Fact]
        public async Task SingleNamedExport()
        {
            await Fixture.Host.BeginFunctionAsync("SingleNamedExport", new JObject());
            await WaitForTraceAsync("SingleNamedExport",
                log => log.FormattedMessage.Contains("Exports: IsObject=true, Count=1"));
        }

        [Fact]
        public async Task HttpTriggerToBlob()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/HttpTriggerToBlob?suffix=TestSuffix"),
                Method = HttpMethod.Post,
            };
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

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
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
        [InlineData("application/json", "{\"test_time\": \"2026-04-20T00:00:00.000Z\", \"test_bool\": \"true\" }", "rawresponse")]
        [InlineData("application/json", "{\"test_time\": \"2016-03-31T07:02:00+07:00\", \"test_bool\": \"true\" }", "rawresponse")]
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

            JObject input = new JObject()
            {
                { "scenario", scenario },
                { "value", JToken.FromObject(body) },
                { "contenttype", expectedContentType }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

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

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(2000, body.Length);
        }

        [Theory(Skip = "Content negotiation not working currently")]
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

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(accept, response.Content.Headers.ContentType.MediaType);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedBody, body);
        }

        [Fact]
        public async Task HttpTrigger_Get_Succeeds()
        {
            string key = await Fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/httptrigger?code={key}&name=Mathew%20Charles&location=Seattle"),
                Method = HttpMethod.Get,
            };
            request.Headers.Add("test-header", "Test Request Header");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

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
        public async Task HttpTrigger_MalformedJsonBody_Succeeds()
        {
            string key = await Fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/httptrigger?code={key}"),
                Method = HttpMethod.Post,
            };
            string json = "} not json";
            request.Content = new StringContent(json, Encoding.UTF8, "AppLication/json");

            var response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("string", (string)resultObject["reqBodyType"]);
            Assert.Equal(json, (string)resultObject["reqBody"]);
            Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            Assert.Equal(json, (string)resultObject["reqRawBody"]);
        }

        [Fact]
        public async Task HttpTriggerExpressApi_SendStatus_Succeeds()
        {
            string key = await Fixture.Host.GetFunctionSecretAsync("HttpTriggerExpressApi");
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/httptriggerexpressapi?code={key}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("scenario", "sendStatus");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task HttpTriggerPromise_ReturnFromPromise_Succeeds()
        {
            string key = await Fixture.Host.GetFunctionSecretAsync("HttpTriggerPromise");
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://localhost/api/httptriggerpromise?code={key}"),
                Method = HttpMethod.Get,
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("returned from promise", body);
        }

        [Fact(Skip = "Needs investigation")]
        public async Task HttpTrigger_Scenarios_ResBinding()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.Headers.Add("scenario", "resbinding");
            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("test", await response.Content.ReadAsAsync<string>());
        }

        [Fact(Skip = "Needs investigation")]
        public async Task HttpTrigger_Scenarios_NullBody()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };
            request.Headers.Add("scenario", "nullbody");
            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Null(response.Content);
        }

        [Fact(Skip = "Needs investigation")]
        public async Task HttpTrigger_Scenarios_ScalarReturn_InBody()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format("http://localhost/api/httptrigger-scenarios")),
                Method = HttpMethod.Post,
            };

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

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal(123, await response.Content.ReadAsAsync<int>());
        }

        [Fact(Skip = "Needs investigation")]
        public async Task HttpTrigger_Scenarios_ScalarReturn()
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger-scenarios"),
                Method = HttpMethod.Post,
            };

            JObject input = new JObject()
            {
                { "scenario", "echo" },
                { "value", 123 }
            };
            request.Content = new StringContent(input.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal(123, await response.Content.ReadAsAsync<int>());
        }

        [Fact(Skip = "Needs investigation")]
        public async Task HttpTrigger_Post_PlainText()
        {
            string testData = Guid.NewGuid().ToString();
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = new Uri("http://localhost/api/httptrigger"),
                Method = HttpMethod.Post,
                Content = new StringContent(testData)
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("string", (string)resultObject["reqBodyType"]);
            Assert.Equal(testData, (string)resultObject["reqBody"]);

            // TODO: reevaluate raw body
            // Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            // Assert.Equal(testData, (string)resultObject["reqRawBody"]);
            Assert.Equal("text/plain", resultObject["reqHeaders"]["content-type"]);
        }

        [Fact(Skip = "Needs investigation")]
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
                RequestUri = new Uri("http://localhost/api/httptrigger"),
                Method = HttpMethod.Post,
                Content = new StringContent(rawBody)
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("object", (string)resultObject["reqBodyType"]);
            Assert.False((bool)resultObject["reqBodyIsArray"]);

            Assert.Equal("Mathew Charles", (string)resultObject["reqBody"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["reqBody"]["location"]);

            Assert.Equal("Mathew Charles", (string)resultObject["bindingData"]["name"]);
            Assert.Equal("Seattle", (string)resultObject["bindingData"]["location"]);

            // TODO: reevaluate raw body
            // Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            // Assert.Equal(rawBody, (string)resultObject["reqRawBody"]);
        }

        [Fact(Skip = "Needs investigation")]
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
                RequestUri = new Uri("http://localhost/api/httptrigger"),
                Method = HttpMethod.Post,
                Content = new StringContent(rawBody)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            Debug.WriteLine(body);
            JObject resultObject = JObject.Parse(body);
            Assert.Equal("object", (string)resultObject["reqBodyType"]);
            Assert.True((bool)resultObject["reqBodyIsArray"]);
            JArray resultArray = (JArray)resultObject["reqBody"];
            Assert.Equal(3, resultArray.Count);

            JObject item = (JObject)resultArray[2];
            Assert.Equal("Curly", (string)item["name"]);
            resultArray = (JArray)item["pets"];
            Assert.Equal(2, resultArray.Count);

            // Assert.Equal("string", (string)resultObject["reqRawBodyType"]);
            // Assert.Equal(rawBody, (string)resultObject["reqRawBody"]);
        }

        [Fact(Skip = "Needs investigation")]
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

            HttpResponseMessage response = await Fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType.MediaType);
            var array = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(0, array[0]);
            Assert.Equal(1, array[1]);
        }

        [Fact]
        public void TimerTrigger()
        {
            var logs = Fixture.Host.GetScriptHostLogMessages(LogCategories.CreateFunctionUserCategory("TimerTrigger"));
            Assert.Contains(logs, log => log.FormattedMessage.Contains("Timer function ran!"));
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

            await Fixture.Host.BeginFunctionAsync("MultipleOutputs", input);

            // verify all 3 output blobs were written
            var blob = Fixture.TestOutputContainer.GetBlockBlobReference(id1);
            await TestHelpers.WaitForBlobAsync(blob);
            string blobContent = await blob.DownloadTextAsync();
            // TODO: why required?
            Assert.Equal("Test Blob 1", blobContent.TrimEnd('\0').Trim('"'));

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id2);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 2", blobContent.TrimEnd('\0').Trim('"'));

            blob = Fixture.TestOutputContainer.GetBlockBlobReference(id3);
            await TestHelpers.WaitForBlobAsync(blob);
            blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Blob 3", blobContent.TrimEnd('\0').Trim('"'));
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

            await Fixture.Host.BeginFunctionAsync("MultipleInputs", input);

            // verify the correct output blob was written
            var blob = Fixture.TestOutputContainer.GetBlockBlobReference(id);
            await TestHelpers.WaitForBlobAsync(blob);
            var blobContent = await blob.DownloadTextAsync();
            Assert.Equal("Test Entity 1, Test Entity 2", Utility.RemoveUtf8ByteOrderMark(blobContent.Trim()));
        }

#if APIHUB
        [Fact( Skip = "unsupported" )]
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

        [Fact( Skip = "unsupported" )]
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
        
#endif
        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Node", "node", LanguageWorkerConstants.NodeLanguageWorkerName)
            {
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.AddAzureStorage()
                    .Services.Configure<ScriptJobHostOptions>(o =>
                    {
                        o.Functions = new[]
                        {
                            "BlobTriggerToBlob",
                            "HttpTrigger",
                            "HttpTrigger-Scenarios",
                            "HttpTriggerExpressApi",
                            "HttpTriggerPromise",
                            "HttpTriggerToBlob",
                            "Invalid",
                            "ManualTrigger",
                            "MultipleExports",
                            "MultipleOutputs",
                            "MultipleInputs",
                            "QueueTriggerByteArray",
                            "QueueTriggerToBlob",
                            "SingleNamedExport",
                            "TableIn",
                            "TableOut",
                            "TimerTrigger",
                            "Scenarios"
                        };
                    });
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