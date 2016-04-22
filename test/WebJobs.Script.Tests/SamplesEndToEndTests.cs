// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public class SamplesEndToEndTests : IClassFixture<SamplesEndToEndTests.TestFixture>
    {
        private TestFixture _fixture;

        public SamplesEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference("result");
            outputBlob.DeleteIfExists();

            CloudBlobContainer inputContainer = _fixture.BlobClient.GetContainerReference("samples-input");
            CloudBlockBlob statusBlob = inputContainer.GetBlockBlobReference("status");
            statusBlob.UploadText("{ \"level\": 4, \"detail\": \"All systems are normal :)\" }");

            string uri = "admin/functions/manualtrigger";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add("x-functions-key", "t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy");
            request.Content = new StringContent("{ 'input': 'Hello Manual Trigger!' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // wait for completion
            string result = await TestHelpers.WaitForBlobAsync(outputBlob);
            Assert.Equal("All systems are normal :)", result);
        }

        [Fact]
        public async Task Home_Get_Succeeds()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task HttpTrigger_Get_Succeeds()
        {
            string uri = "api/httptrigger?code=hyexydhln844f2mb7hgsup2yf8dowlb0885mbiq1&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew", body);

            // verify that the secondary key also works
            uri = "api/httptrigger?code=m3vg59azmxzxb8ofwwjeg738f654qjve0bwmyhte&name=Mathew";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task HttpTrigger_Disabled_SucceedsWithAdminKey()
        {
            // first try with function key only - expect 404
            string uri = "api/httptrigger-disabled?code=zlnu496ve212kk1p84ncrtdvmtpembduqp25ajjc";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // now try with admin key
            uri = "api/httptrigger-disabled?code=t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!", body);

            // verify secondary admin key also works
            uri = "api/httptrigger-disabled?code=z3dlq50s00cb3q3k11nil7xyt29ebst2n8rtn0t3";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GenericWebHook_CSharp_Post_Succeeds()
        {
            string uri = "api/webhook-generic-csharp?code=827bdzxhqy3xc62cxa2hmfsh6gxzhg30s5pi64tu";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'Value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact]
        public async Task AzureWebHook_CSharp_Post_Succeeds()
        {
            string uri = "api/webhook-azure-csharp?code=yKjiimZjC1FQoGlaIj8TUfGltnPE/f2LhgZNq6Fw9/XfAOGHmSgUlQ==";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent(Resources.AzureWebHookEventRequest);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            
            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Unresolved", jsonObject["status"]);
        }

        [Fact]
        public async Task HttpTriggerWithObject_CSharp_Post_Succeeds()
        {
            string uri = "api/httptriggerwithobject-csharp?code=zlnu496ve212kk1p84ncrtdvmtpembduqp25ajjc";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'SenderName': 'Fabio' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Hello, Fabio", jsonObject["Greeting"]);
        }

        [Fact]
        public async Task GenericWebHook_Post_Succeeds()
        {
            string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact]
        public async Task GenericWebHook_Post_AdminKey_Succeeds()
        {
            // Verify that sending the admin key bypasses WebHook auth
            string uri = "api/webhook-generic?code=t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact]
        public async Task QueueTriggerBatch_Succeeds()
        {
            // write the input message
            CloudQueue inputQueue = _fixture.QueueClient.GetQueueReference("samples-batch");
            string id = Guid.NewGuid().ToString();
            JObject jsonObject = new JObject
            {
                { "id", id }
            };
            var message = new CloudQueueMessage(jsonObject.ToString(Formatting.None));
            await inputQueue.AddMessageAsync(message);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAsync(outputBlob);

            jsonObject = JObject.Parse(result);
            Assert.Equal(id, (string)jsonObject["id"]);
        }

        [Fact]
        public async Task BlobTriggerBatch_Succeeds()
        {
            // write input blob
            CloudBlobContainer inputContainer = _fixture.BlobClient.GetContainerReference("samples-batch");
            string blobName = Guid.NewGuid().ToString();
            string testData = "This is a test";
            CloudBlockBlob inputBlob = inputContainer.GetBlockBlobReference(blobName);
            await inputBlob.UploadTextAsync(testData);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(blobName);
            string result = await TestHelpers.WaitForBlobAsync(outputBlob);

            // verify results
            Assert.Equal(testData, result.Trim());
        }

        public class TestFixture
        {
            public TestFixture()
            {
                HttpConfiguration config = new HttpConfiguration();

                WebHostSettings settings = new WebHostSettings
                {
                    IsSelfHost = true,
                    ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\sample"),
                    LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                    SecretsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\src\WebJobs.Script.WebHost\App_Data\Secrets")
                };
                WebApiConfig.Register(config, settings);

                HttpServer server = new HttpServer(config);
                this.HttpClient = new HttpClient(server);
                this.HttpClient.BaseAddress = new Uri("https://localhost/");

                string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString("Storage");
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                BlobClient = storageAccount.CreateCloudBlobClient();
                QueueClient = storageAccount.CreateCloudQueueClient();

                WaitForHost();
            }

            public CloudBlobClient BlobClient { get; set; }

            public CloudQueueClient QueueClient { get; set; }

            public HttpClient HttpClient { get; set; }

            private void WaitForHost()
            {
                TestHelpers.Await(() =>
                {
                    return IsHostRunning();
                }).Wait();
            }

            private bool IsHostRunning()
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

                HttpResponseMessage response = this.HttpClient.SendAsync(request).Result;
                return response.StatusCode == HttpStatusCode.NoContent;
            }
        }
    }
}
