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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_Node : IClassFixture<SamplesEndToEndTests_Node.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests_Node(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task ServiceBusQueueTrigger_Succeeds()
        {
            await Task.CompletedTask;
            //string queueName = "samples-input";
            //string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            //var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);
            //namespaceManager.DeleteQueue(queueName);
            //namespaceManager.CreateQueue(queueName);

            //var client = Microsoft.ServiceBus.Messaging.QueueClient.CreateFromConnectionString(connectionString, queueName);

            //// write a start message to the queue to kick off the processing
            //int max = 3;
            //string id = Guid.NewGuid().ToString();
            //JObject message = new JObject
            //{
            //    { "count", 1 },
            //    { "max", max },
            //    { "id", id }
            //};
            //using (Stream stream = new MemoryStream())
            //using (TextWriter writer = new StreamWriter(stream))
            //{
            //    writer.Write(message.ToString());
            //    writer.Flush();
            //    stream.Position = 0;

            //    client.Send(new BrokeredMessage(stream) { ContentType = "text/plain" });
            //}

            //client.Close();

            //// wait for function to execute and produce its result blob
            //CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            //CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            //string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            //Assert.Equal(string.Format("{0} messages processed", max), result.Trim());
        }

        [Fact(Skip = "Not currently supported.")]
        public void ServiceBusTopicTrigger_Succeeds()
        {
            //    string topicName = "samples-topic";
            //    string subscriptionName = "samples";
            //    string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            //    var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            //    if (!namespaceManager.TopicExists(topicName))
            //    {
            //        namespaceManager.CreateTopic(topicName);
            //    }

            //    if (!namespaceManager.SubscriptionExists(topicName, subscriptionName))
            //    {
            //        namespaceManager.CreateSubscription(topicName, subscriptionName);
            //    }

            //    var client = Microsoft.ServiceBus.Messaging.TopicClient.CreateFromConnectionString(connectionString, topicName);

            //    // write a start message to the queue to kick off the processing
            //    string id = Guid.NewGuid().ToString();
            //    string value = Guid.NewGuid().ToString();
            //    JObject message = new JObject
            //    {
            //        { "id", id },
            //        { "value", value }
            //    };
            //    using (Stream stream = new MemoryStream())
            //    using (TextWriter writer = new StreamWriter(stream))
            //    {
            //        writer.Write(message.ToString());
            //        writer.Flush();
            //        stream.Position = 0;

            //        client.Send(new BrokeredMessage(stream) { ContentType = "text/plain" });
            //    }

            //    client.Close();

            //    // wait for function to execute and produce its result blob
            //    CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            //    CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            //    string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            //    Assert.Equal(value, result.Trim());
        }

        [Fact(Skip = "Not currently supported.")]
        public void ServiceBusTopicTrigger_ManualInvoke_Succeeds()
        {
            //    string topicName = "samples-topic";
            //    string subscriptionName = "samples";
            //    string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            //    var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            //    if (!namespaceManager.TopicExists(topicName))
            //    {
            //        namespaceManager.CreateTopic(topicName);
            //    }

            //    if (!namespaceManager.SubscriptionExists(topicName, subscriptionName))
            //    {
            //        namespaceManager.CreateSubscription(topicName, subscriptionName);
            //    }

            //    string uri = "admin/functions/servicebustopictrigger";
            //    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            //    request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());
            //    string id = Guid.NewGuid().ToString();
            //    string value = Guid.NewGuid().ToString();
            //    JObject input = new JObject()
            //    {
            //        {
            //            "input", new JObject()
            //            {
            //                { "id", id },
            //                { "value", value }
            //            }.ToString()
            //        }
            //    };
            //    string json = input.ToString();
            //    request.Content = new StringContent(json);
            //    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            //    HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            //    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            //    var client = Microsoft.ServiceBus.Messaging.TopicClient.CreateFromConnectionString(connectionString, topicName);

            //    // wait for function to execute and produce its result blob
            //    CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            //    CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            //    string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            //    Assert.Equal(value, result.Trim());
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
            }, userMessageCallback: _fixture.Host.GetLog);

            Assert.True(logs.Contains("IsArray true"));
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            string inId = Guid.NewGuid().ToString();
            string outId = Guid.NewGuid().ToString();
            CloudBlockBlob statusBlob = outputContainer.GetBlockBlobReference(inId);
            JObject testData = new JObject()
            {
                { "first", "Mathew" },
                { "last", "Charles" }
            };
            await statusBlob.UploadTextAsync(testData.ToString(Formatting.None));

            JObject input = new JObject()
            {
                { "inId", inId },
                { "outId", outId }
            };

            await _fixture.Host.BeginFunctionAsync("manualtrigger", input);

            // wait for completion
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(outId);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);
            Assert.Equal("Mathew Charles", result);
        }

        [Fact]
        public async Task HttpTrigger_Get_Succeeds()
        {
            await InvokeAndValidateHttpTrigger("HttpTrigger");
        }

        private async Task InvokeAndValidateHttpTrigger(string functionName)
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync($"{functionName}");
            string uri = $"api/{functionName}?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew", body);

            // verify request also succeeds with master key
            string masterKey = await _fixture.Host.GetMasterKeyAsync();
            uri = $"api/{functionName}?code={masterKey}&name=Mathew";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task<HttpResponseMessage> InvokeHttpTrigger(string functionName)
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync($"{functionName}");
            string uri = $"api/{functionName}?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            return await _fixture.Host.HttpClient.SendAsync(request);
        }

        [Fact]
        public async Task HttpTrigger_DuplicateQueryParams_Succeeds()
        {

            string functionKey = await _fixture.Host.GetFunctionSecretAsync("httptrigger");
            string uri = $"api/httptrigger?code={functionKey}&name=Mathew&name=Amy";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew,Amy", body);
        }

        [Fact]
        public async Task HttpTrigger_CustomRoute_Get_ReturnsExpectedResponse()
        {
            var id = "4e2796ae-b865-4071-8a20-2a15cbaf856c";
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-CustomRoute-Get");
            string uri = $"api/node/products/electronics/{id}?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            JArray products = JArray.Parse(json);
            Assert.Equal(1, products.Count);
            var product = products[0];
            Assert.Equal("electronics", (string)product["category"]);
            Assert.Equal(id, (string)product["id"]);

            // test optional route param (id)
            uri = $"api/node/products/electronics?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            products = JArray.Parse(json);
            Assert.Equal(2, products.Count);
            var logs = _fixture.Host.GetScriptHostLogMessages(LogCategories.CreateFunctionUserCategory("HttpTrigger-CustomRoute-Get"));
            var log = logs.Single(p => p.FormattedMessage.Contains($"category: electronics id: <empty>"));
            _fixture.Host.ClearLogMessages();

            // test optional route param (category)
            uri = $"api/node/products?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            products = JArray.Parse(json);
            Assert.Equal(3, products.Count);
            logs = _fixture.Host.GetScriptHostLogMessages(LogCategories.CreateFunctionUserCategory("HttpTrigger-CustomRoute-Get"));
            log = logs.Single(p => p.FormattedMessage.Contains($"category: <empty> id: <empty>"));

            // test a constraint violation (invalid id)
            uri = $"api/node/products/electronics/notaguid?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // test a constraint violation (invalid category)
            uri = $"api/node/products/999/{id}?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact(Skip = "http://github.com/Azure/azure-functions-host/issues/2812")]
        public async Task HttpTrigger_CustomRoute_Post_ReturnsExpectedResponse()
        {

            string id = Guid.NewGuid().ToString();
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-CustomRoute-Post");
            string uri = $"api/node/products/housewares/{id}?code={functionKey}";
            JObject product = new JObject
            {
                { "id", id },
                { "name", "Waffle Maker Pro" },
                { "category", "Housewares" }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(product.ToString())
            };

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            string path = $"housewares/{id}";
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(path);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);
            JObject resultProduct = JObject.Parse(Utility.RemoveUtf8ByteOrderMark(result));
            Assert.Equal(id, (string)resultProduct["id"]);
            Assert.Equal((string)product["name"], (string)resultProduct["name"]);
        }

        [Fact]
        public async Task SharedDirectory_ReloadsOnFileChange()
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger");

            string uri = $"api/httptrigger?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            string initialTimestamp = response.Headers.GetValues("Shared-Module").First();

            // make the request again and verify the timestamp is the same
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            string timestamp = response.Headers.GetValues("Shared-Module").First();
            Assert.Equal(initialTimestamp, timestamp);

            // now "touch" a file in the shared directory to trigger a restart
            string sharedModulePath = Path.Combine(_fixture.Host.ScriptPath, "Shared\\test.js");
            File.SetLastWriteTimeUtc(sharedModulePath, DateTime.UtcNow);

            // wait for the module to be reloaded
            await TestHelpers.Await(async () =>
            {
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                timestamp = response.Headers.GetValues("Shared-Module").First();
                return initialTimestamp != timestamp;
            }, timeout: 5000, pollingInterval: 500);
            Assert.NotEqual(initialTimestamp, timestamp);

            initialTimestamp = timestamp;
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            timestamp = response.Headers.GetValues("Shared-Module").First();
            Assert.Equal(initialTimestamp, timestamp);
        }

        [Fact]
        public async Task NodeProcess_Different_AfterHostRestart()
        {
            IEnumerable<int> nodeProcessesBefore = Process.GetProcessesByName("node").Select(p => p.Id);
            // Trigger a restart
            await _fixture.Host.RestartAsync(CancellationToken.None);

            await HttpTrigger_Get_Succeeds();
            IEnumerable<int> nodeProcessesAfter = Process.GetProcessesByName("node").Select(p => p.Id);
            // Verify number of node processes before and after restart are the same.
            Assert.Equal(nodeProcessesBefore.Count(), nodeProcessesAfter.Count());
            // Verify node process is different after host restart
            var result = nodeProcessesBefore.Where(pId1 => !nodeProcessesAfter.Any(pId2 => pId2 == pId1));
            Assert.Equal(1, result.Count());
        }

        [Fact]
        public async Task HttpTrigger_Disabled_SucceedsWithAdminKey()
        {

            // first try with function key only - expect 404
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-Disabled");
            string uri = $"api/httptrigger-disabled?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // now try with admin key
            string masterKey = await _fixture.Host.GetMasterKeyAsync();
            uri = $"api/httptrigger-disabled?code={masterKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!", body);
        }

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
                Environment.SetEnvironmentVariable("AzureWebJobs.HttpTrigger-Disabled.Disabled", "1");
            }

            // Microsoft.Azure.WebJobs.Extensions.EventHubs
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample\node"), "samples", LanguageWorkerConstants.NodeLanguageWorkerName)
            {
            }

            protected override ExtensionPackageReference[] GetExtensionsToInstall()
            {
                return new ExtensionPackageReference[]
                {
                    new ExtensionPackageReference
                    {
                        Id = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                        Version = "3.0.0-beta*"
                    }
                };
            }

            public override void ConfigureJobHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureJobHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "EventHubTrigger",
                        "HttpTrigger",
                        "HttpTrigger-CustomRoute-Get",
                        "HttpTrigger-Disabled",
                        "HttpTrigger-Identities",
                        "ManualTrigger"
                    };
                });
            }
        }
    }
}