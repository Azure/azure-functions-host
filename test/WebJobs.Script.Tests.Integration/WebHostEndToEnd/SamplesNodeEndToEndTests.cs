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
    public class SamplesNodeEndToEndTests : IClassFixture<SamplesNodeEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesNodeEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        public object AuthorizationLevelAttribute { get; private set; }

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

            // test optional route param (category)
            uri = $"api/node/products?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            products = JArray.Parse(json);
            Assert.Equal(3, products.Count);

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

            // verify route parameters were part of binding data
            var logs = _fixture.Host.GetLogMessages(LogCategories.CreateFunctionUserCategory("HttpTrigger-CustomRoute-Get"));
            var log = logs.Single(p => p.FormattedMessage.Contains($"category: electronics id: {id}"));
            Assert.NotNull(log);
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
        public async Task SharedDirectory_Node_ReloadsOnFileChange()
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

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples", LanguageWorkerConstants.NodeLanguageWorkerName)
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