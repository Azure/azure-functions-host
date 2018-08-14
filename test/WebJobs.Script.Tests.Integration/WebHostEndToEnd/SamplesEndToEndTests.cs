// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(SamplesEndToEndTests))]
    public class SamplesEndToEndTests : IClassFixture<SamplesEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        public object AuthorizationLevelAttribute { get; private set; }

        [Fact]
        public async Task SetHostState_Offline_Succeeds()
        {
            // verify host is up and running
            var response = await GetHostStatusAsync();
            var hostStatus = response.Content.ReadAsAsync<HostStatus>();

            // verify functions can be invoked
            await InvokeAndValidateHttpTrigger("HttpTrigger");

            // take host offline
            await SetHostStateAsync("offline");

            // when testing taking the host offline doesn't seem to stop all
            // application services, so we issue a restart
            await RestartHostAsync();

            // wait for the host to go offline
            await AwaitHostStateAsync(ScriptHostState.Offline);

            // verify that when offline function requests return 503
            response = await InvokeHttpTrigger("HttpTrigger");
            await VerifyOfflineResponse(response);

            // verify that the root returns 503 when offline
            var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            await VerifyOfflineResponse(response);

            // bring host back online
            await SetHostStateAsync("running");

            await AwaitHostStateAsync(ScriptHostState.Running);

            // verify functions can be invoked
            await InvokeAndValidateHttpTrigger("HttpTrigger");
        }

        private static async Task VerifyOfflineResponse(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType);
            string content = await response.Content.ReadAsStringAsync();
            Assert.True(content.Contains("Host is offline"));
        }

        [Fact]
        public async Task AdminRequests_PutHostInDebugMode()
        {
            var debugSentinelFilePath = Path.Combine(_fixture.Host.LogPath, "Host", ScriptConstants.DebugSentinelFileName);

            File.Delete(debugSentinelFilePath);

            HttpResponseMessage response = await GetHostStatusAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.True(File.Exists(debugSentinelFilePath));
            var lastModified = File.GetLastWriteTime(debugSentinelFilePath);

            await Task.Delay(100);

            response = await GetHostStatusAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(lastModified < File.GetLastWriteTime(debugSentinelFilePath));
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
        public async Task Home_Get_Succeeds()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Home_Get_WithHomepageDisabled_Succeeds()
        {
            using (new TestScopedSettings(_settingsManager, EnvironmentSettingNames.AzureWebJobsDisableHomepage, bool.TrueString))
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
        }

        [Fact]
        public async Task Home_Get_InAzureEnvironment_AsInternalRequest_ReturnsNoContent()
        {
            // Pings to the site root should not return the homepage content if they are internal requests.
            // This test sets a website instance Id which means that we'll go down the IsAzureEnvironment = true codepath
            // but the sent request does NOT include an X-ARR-LOG-ID header. This indicates the request was internal.

            using (new TestScopedSettings(_settingsManager, EnvironmentSettingNames.AzureWebsiteInstanceId, "123"))
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
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
        public async Task HostPing_Succeeds()
        {
            string uri = "admin/host/ping";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task HostLog_Anonymous_Fails()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz");
            request.Content = new StringContent("[]");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            request.Content = new StringContent("[]");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task HostLog_AdminLevel_Succeeds()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());
            var logs = new HostLogEntry[]
            {
                new HostLogEntry
                {
                    Level = System.Diagnostics.TraceLevel.Verbose,
                    Source = "ScaleController",
                    Message = string.Format("Test Verbose log {0}", Guid.NewGuid().ToString())
                },
                new HostLogEntry
                {
                    Level = System.Diagnostics.TraceLevel.Info,
                    Source = "ScaleController",
                    Message = string.Format("Test Info log {0}", Guid.NewGuid().ToString())
                },
                new HostLogEntry
                {
                    Level = System.Diagnostics.TraceLevel.Warning,
                    Source = "ScaleController",
                    Message = string.Format("Test Warning log {0}", Guid.NewGuid().ToString())
                },
                new HostLogEntry
                {
                    Level = System.Diagnostics.TraceLevel.Error,
                    Source = "ScaleController",
                    FunctionName = "TestFunction",
                    Message = string.Format("Test Error log {0}", Guid.NewGuid().ToString())
                }
            };
            var serializer = new JsonSerializer();
            var writer = new StringWriter();
            serializer.Serialize(writer, logs);
            var json = writer.ToString();
            request.Content = new StringContent(json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await Task.Delay(1000);

            var hostLogs = _fixture.Host.GetLogMessages();
            foreach (var expectedLog in logs.Select(p => p.Message))
            {
                Assert.Equal(1, hostLogs.Count(p => p.FormattedMessage != null && p.FormattedMessage.Contains(expectedLog)));
            }
        }

        [Fact]
        public async Task HostLog_SingletonLog_ReturnsBadRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());
            var log = new HostLogEntry
            {
                Level = System.Diagnostics.TraceLevel.Verbose,
                Source = "ScaleController",
                Message = string.Format("Test Verbose log {0}", Guid.NewGuid().ToString())
            };
            request.Content = new StringContent(log.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadAsStringAsync();
            Assert.Contains(error, "An array of log entry objects is expected.");
        }

        [Fact]
        public async Task HostStatus_AdminLevel_Succeeds()
        {
            string uri = "admin/host/status";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());

            HttpResponseMessage response = await GetHostStatusAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            JObject jsonContent = JObject.Parse(content);

            Assert.Equal(4, jsonContent.Properties().Count());
            AssemblyFileVersionAttribute fileVersionAttr = typeof(HostStatus).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            Assert.True(((string)jsonContent["id"]).Length > 0);
            string expectedVersion = fileVersionAttr.Version;
            Assert.Equal(expectedVersion, (string)jsonContent["version"]);
            string expectedVersionDetails = Utility.GetInformationalVersion(typeof(ScriptHost));
            Assert.Equal(expectedVersionDetails, (string)jsonContent["versionDetails"]);
            var state = (string)jsonContent["state"];
            Assert.True(state == "Running" || state == "Created" || state == "Initialized");

            // Now ensure XML content works
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());
            request.Headers.Add("Accept", "text/xml");

            response = await _fixture.Host.HttpClient.SendAsync(request);
            content = await response.Content.ReadAsStringAsync();

            string ns = "http://schemas.datacontract.org/2004/07/Microsoft.Azure.WebJobs.Script.WebHost.Models";
            XDocument doc = XDocument.Parse(content);
            var node = doc.Descendants(XName.Get("Version", ns)).Single();
            Assert.Equal(expectedVersion, node.Value);
            node = doc.Descendants(XName.Get("VersionDetails", ns)).Single();
            Assert.Equal(expectedVersionDetails, node.Value);
            node = doc.Descendants(XName.Get("Id", ns)).Single();
            Assert.True(node.Value.Length > 0);
            node = doc.Descendants(XName.Get("State", ns)).Single();
            Assert.True(node.Value == "Running" || node.Value == "Created" || node.Value == "Initialized");

            node = doc.Descendants(XName.Get("Errors", ns)).Single();
            Assert.True(node.IsEmpty);
        }

        [Fact]
        public async Task HostStatus_AnonymousLevelRequest_Fails()
        {
            string uri = "admin/host/status";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        private async Task<HttpResponseMessage> GetHostStatusAsync()
        {
            string uri = "admin/host/status";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());

            return await _fixture.Host.HttpClient.SendAsync(request);
        }

        private async Task SetHostStateAsync(string state)
        {
            var masterKey = await _fixture.Host.GetMasterKeyAsync();
            var request = new HttpRequestMessage(HttpMethod.Put, "admin/host/state");
            request.Content = new StringContent($"'{state}'");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);
            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(response.StatusCode, HttpStatusCode.Accepted);
        }

        private async Task AwaitHostStateAsync(ScriptHostState state)
        {
            await TestHelpers.Await(async () =>
            {
                var response = await GetHostStatusAsync();
                var hostStatus = response.Content.ReadAsAsync<HostStatus>();
                return string.Compare(hostStatus.Result.State, state.ToString(), StringComparison.OrdinalIgnoreCase) == 0;
            });
        }

        private async Task<HttpResponseMessage> RestartHostAsync()
        {
            string uri = "admin/host/restart";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());

            return await _fixture.Host.HttpClient.SendAsync(request);
        }

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
                Environment.SetEnvironmentVariable("AzureWebJobs.HttpTrigger-Disabled.Disabled", "1");
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName);
            }

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples", null)
            {
            }

            public override void ConfigureJobHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureJobHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger",
                        "HttpTrigger-CustomRoute-Get",
                        "HttpTrigger-Disabled",
                        "HttpTrigger-Java",
                        "ManualTrigger"
                    };
                });
            }

            protected override async Task CreateTestStorageEntities()
            {
                // Don't call base.
                var table = TableClient.GetTableReference("samples");
                await table.CreateIfNotExistsAsync();

                var batch = new TableBatchOperation();
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "1", Title = "Test Entity 1", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "2", Title = "Test Entity 2", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "3", Title = "Test Entity 3", Status = 1 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "4", Title = "Test Entity 4", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "5", Title = "Test Entity 5", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "6", Title = "Test Entity 6", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "7", Title = "Test Entity 7", Status = 0 });
                await table.ExecuteBatchAsync(batch);
            }

            private class TestEntity : TableEntity
            {
                public string Title { get; set; }

                public int Status { get; set; }
            }
        }
    }
}