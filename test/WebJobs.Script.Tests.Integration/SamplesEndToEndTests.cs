#if SERVICEBUS
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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Tests.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(SamplesEndToEndTests))]
    public class SamplesEndToEndTests : IClassFixture<SamplesEndToEndTests.TestFixture>
    {
        internal const string MasterKey = "t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy";

        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task EventHubTrigger()
        {
            TestHelpers.ClearFunctionLogs("EventHubTrigger");

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
            ServiceBusConnectionStringBuilder builder = new ServiceBusConnectionStringBuilder(connectionString);
            EventHubClient eventHubClient;
            if (!string.IsNullOrWhiteSpace(builder.EntityPath))
            {
                eventHubClient = EventHubClient.CreateFromConnectionString(connectionString);
            }
            else
            {
                string eventHubPath = _settingsManager.GetSetting("AzureWebJobsEventHubPath");
                eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, eventHubPath);
            }

            await eventHubClient.SendBatchAsync(events);

            string logs = null;
            await TestHelpers.Await(() =>
            {
                // wait until all of the 3 of the unique IDs sent
                // above have been processed
                logs = string.Join("\r\n", TestHelpers.GetFunctionLogsAsync("EventHubTrigger", throwOnNoLogs: false).Result);
                return ids.All(p => logs.Contains(p));
            });

            Assert.True(logs.Contains("IsArray true"));
        }

        [Fact]
        public async Task AdminRequests_PutHostInDebugMode()
        {
            var debugSentinelFilePath = Path.Combine(_fixture.HostSettings.LogPath, "Host", ScriptConstants.DebugSentinelFileName);

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
        public async Task ManualTrigger_CSharp_Invoke_Succeeds()
        {
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            string inId = Guid.NewGuid().ToString();
            string outId = Guid.NewGuid().ToString();
            CloudBlockBlob statusBlob = outputContainer.GetBlockBlobReference(inId);
            statusBlob.UploadText("Hello C#!");

            string uri = "admin/functions/manualtrigger-csharp";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
            JObject input = new JObject()
            {
                {
                    "input", new JObject()
                    {
                        { "InId", inId },
                        { "OutId", outId }
                    }.ToString()
                }
            };
            string json = input.ToString();
            request.Content = new StringContent(json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // wait for completion
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(outId);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);
            Assert.Equal("Hello C#!", Utility.RemoveUtf8ByteOrderMark(result));
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
            statusBlob.UploadText(testData.ToString(Formatting.None));

            string uri = "admin/functions/manualtrigger";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
            JObject input = new JObject()
            {
                {
                    "input", new JObject()
                    {
                        { "inId", inId },
                        { "outId", outId }
                    }.ToString()
                }
            };
            string json = input.ToString();
            request.Content = new StringContent(json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // wait for completion
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(outId);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);
            Assert.Equal("Mathew Charles", result);
        }

        [Fact]
        public async Task Home_Get_Succeeds()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Home_Get_WithHomepageDisabled_Succeeds()
        {
            using (new TestScopedSettings(_settingsManager, EnvironmentSettingNames.AzureWebJobsDisableHomepage, bool.TrueString))
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

                HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
        }

        [Fact]
        public async Task HttpTrigger_CSharp_Poco_Post_Succeeds()
        {
            string uri = "api/httptrigger-csharp-poco?code=zlnu496ve212kk1p84ncrtdvmtpembduqp25ajjc";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);

            string id = Guid.NewGuid().ToString();
            JObject requestBody = new JObject
            {
                { "Id", id },
                { "Value", "Testing" }
            };
            request.Content = new StringContent(requestBody.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            Assert.Equal("Testing", TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
        }

        [Fact]
        public async Task HttpTrigger_CSharp_Poco_Post_Xml_Succeeds()
        {
            string uri = "api/httptrigger-csharp-poco?code=zlnu496ve212kk1p84ncrtdvmtpembduqp25ajjc";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            string id = Guid.NewGuid().ToString();
            request.Content = new StringContent(string.Format("<RequestData xmlns=\"http://functions\"><Id>{0}</Id><Value>Testing</Value></RequestData>", id));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            Assert.Equal("Testing", TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
        }

        [Fact]
        public async Task HttpTrigger_CSharp_Poco_Get_Succeeds()
        {
            string id = Guid.NewGuid().ToString();
            string uri = string.Format("api/httptrigger-csharp-poco?code=zlnu496ve212kk1p84ncrtdvmtpembduqp25ajjc&Id={0}&Value=Testing", id);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            Assert.Equal("Testing", TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
        }

        [Fact]
        public async Task HttpTrigger_Get_Succeeds()
        {
            string uri = "api/httptrigger?code=hyexydhln844f2mb7hgsup2yf8dowlb0885mbiq1&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew", body);

            // verify request also succeeds with master key
            uri = $"api/httptrigger?code={MasterKey}&name=Mathew";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task HttpTrigger_Get_Unauthorized_Fails()
        {
            // valid system (swagger) key
            var swaggerSystemKey = "2yHCHEQX/CYqsPASEvWaRL08xI4afd38aHzzvMid8qozJhwqYzhJmQ==";
            string uri = $"api/httptrigger?code={swaggerSystemKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // verify the key works for the swagger endpoint
            uri = $"admin/host/swagger/default?code={swaggerSystemKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // verify the key works for the swagger endpoint
            var systemKey = "bcnu496ve212kk1p84ncrtdvmtpembduqp25aghe";
            uri = $"admin/host/swagger/default?code={systemKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task HttpTrigger_DuplicateQueryParams_Succeeds()
        {
            string uri = "api/httptrigger?code=hyexydhln844f2mb7hgsup2yf8dowlb0885mbiq1&name=Mathew&name=Amy";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Amy", body);
        }

        [Fact]
        public async Task HttpTrigger_CustomRoute_Get_ReturnsExpectedResponse()
        {
            TestHelpers.ClearFunctionLogs("HttpTrigger-CustomRoute-Get");

            var id = "4e2796ae-b865-4071-8a20-2a15cbaf856c";
            string functionKey = "82fprgh77jlbhcma3yr1zen8uv9yb0i7dwze3np2";
            string uri = $"api/node/products/electronics/{id}?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
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
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            products = JArray.Parse(json);
            Assert.Equal(2, products.Count);

            // test optional route param (category)
            uri = $"api/node/products?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            products = JArray.Parse(json);
            Assert.Equal(3, products.Count);

            // test a constraint violation (invalid id)
            uri = $"api/node/products/electronics/notaguid?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // test a constraint violation (invalid category)
            uri = $"api/node/products/999/{id}?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // verify route parameters were part of binding data
            var logs = await TestHelpers.GetFunctionLogsAsync("HttpTrigger-CustomRoute-Get");
            var log = logs.Single(p => p.Contains($"category: electronics id: {id}"));
            Assert.NotNull(log);
        }

        [Fact]
        public async Task HttpTrigger_CustomRoute_Post_ReturnsExpectedResponse()
        {
            TestHelpers.ClearFunctionLogs("HttpTrigger-CustomRoute-Post");

            string id = Guid.NewGuid().ToString();
            string functionKey = "5u3pyihh8ldfelipm3qdabw69iah0oghgzw8n959";
            string uri = $"api/node/products/housewares/{id}?code={functionKey}";
            JObject product = new JObject
            {
                { "id", id },
                { "name", "Waffle Maker Pro" },
                { "category", "Housewares" }
            };
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(product.ToString())
            };

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
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
            string uri = "api/httptrigger?code=hyexydhln844f2mb7hgsup2yf8dowlb0885mbiq1&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await _fixture.HttpClient.SendAsync(request);
            string initialTimestamp = response.Headers.GetValues("Shared-Module").First();

            // make the request again and verify the timestamp is the same
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.HttpClient.SendAsync(request);
            string timestamp = response.Headers.GetValues("Shared-Module").First();
            Assert.Equal(initialTimestamp, timestamp);

            // now "touch" a file in the shared directory to trigger a restart
            string sharedModulePath = Path.Combine(_fixture.HostSettings.ScriptPath, "Shared\\test.js");
            File.SetLastWriteTimeUtc(sharedModulePath, DateTime.UtcNow);

            // wait for the module to be reloaded
            await TestHelpers.Await(() =>
            {
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = _fixture.HttpClient.SendAsync(request).GetAwaiter().GetResult();
                timestamp = response.Headers.GetValues("Shared-Module").First();
                return initialTimestamp != timestamp;
            }, timeout: 5000, pollingInterval: 1000);
            Assert.NotEqual(initialTimestamp, timestamp);

            initialTimestamp = timestamp;
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.HttpClient.SendAsync(request);
            timestamp = response.Headers.GetValues("Shared-Module").First();
            Assert.Equal(initialTimestamp, timestamp);
        }

        [Fact]
        public async Task HttpTrigger_CSharp_CustomRoute_ReturnsExpectedResponse()
        {
            TestHelpers.ClearFunctionLogs("HttpTrigger-CSharp-CustomRoute");

            string functionKey = "68qkqlughacc6f9n6t4ubk0jq7r5er7pta13yh20";
            string uri = $"api/csharp/products/electronics/123?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            var product = JObject.Parse(json);
            Assert.Equal("electronics", (string)product["Category"]);
            Assert.Equal(123, (int?)product["Id"]);

            // test optional id parameter
            uri = $"api/csharp/products/electronics?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            product = JObject.Parse(json);
            Assert.Equal("electronics", (string)product["Category"]);
            Assert.Null((int?)product["Id"]);

            // test optional category parameter
            uri = $"api/csharp/products?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            product = JObject.Parse(json);
            Assert.Null((string)product["Category"]);
            Assert.Null((int?)product["Id"]);

            // test a constraint violation (invalid id)
            uri = $"api/csharp/products/electronics/1x3?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // test a constraint violation (invalid category)
            uri = $"api/csharp/products/999/123?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // verify route parameters were part of binding data
            var logs = await TestHelpers.GetFunctionLogsAsync("HttpTrigger-CSharp-CustomRoute");
            Assert.True(logs.Any(p => p.Contains("Parameters: category=electronics id=123")));
            Assert.True(logs.Any(p => p.Contains("ProductInfo: Category=electronics Id=123")));
        }

        [Fact]
        public async Task HttpTrigger_Disabled_SucceedsWithAdminKey()
        {
            // first try with function key only - expect 404
            string uri = "api/httptrigger-disabled?code=zlnu496ve212kk1p84ncrtdvmtpembduqp25ajjc";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // now try with admin key
            uri = "api/httptrigger-disabled?code=t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!", body);
        }

        [Fact]
        public async Task HttpTriggerPowerShell_Get_Succeeds()
        {
            string uri = "api/httptrigger-powershell?code=N5rUeecvsqN1Q1lDciR7P8kn3KkQtnNJVlK7H5bev0jO7r5DbAZgvA==&name=testuser";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello testuser", body);
        }

        [Fact]
        public async Task HttpTriggerPowerShellModules_Get_Succeeds()
        {
            string uri = "api/httptrigger-powershell-modules?code=8CTs65hqBcX3DVddZOGkPoksSaIDRck9byv1ATWbqJuOb9h8MrVZzA==&name=testuser";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.True(body.StartsWith("Hello testuser"));
            Assert.True(body.Contains("March 2016"));
        }

        [Fact]
        public async Task GenericWebHook_CSharp_Post_Succeeds()
        {
            string uri = "api/hooks/csharp/generic/test?code=827bdzxhqy3xc62cxa2hmfsh6gxzhg30s5pi64tu";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'Value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar Action: test", jsonObject["result"]);
        }

        [Fact]
        public async Task GenericWebHook_CSharp_Dynamic_Post_Succeeds()
        {
            string uri = "api/webhook-generic-csharp-dynamic?code=88adV34UZhaydCLOjMzbMgUtXh3stBnL8LFcL9R17DL8HAY8PVhpZA==";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'Value': 'Foobar' }");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("\"Value: Foobar\"", body);
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
        public async Task GenericWebHook_Post_NamedKey_Succeeds()
        {
            // Authenticate using a named key (client id)
            string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a6&clientid=testclient";
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
        public async Task GenericWebHook_Post_NamedKey_Headers_Succeeds()
        {
            // Authenticate using values specified via headers,
            // rather than URI query params
            string uri = "api/webhook-generic";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
            request.Headers.Add("x-functions-clientid", "testclient");
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
        public async Task GenericWebHook_Post_NamedKeyInHeader_Succeeds()
        {
            // Authenticate using a named key (client id)
            string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a6";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(WebHost.WebHooks.WebHookReceiverManager.FunctionsClientIdHeaderName, "testclient");
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
            inputQueue.CreateIfNotExists();

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
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            jsonObject = JObject.Parse(result);
            Assert.Equal(id, (string)jsonObject["id"]);
        }

        [Fact]
        public async Task QueueTriggerPowerShell_Succeeds()
        {
            // write the input message
            CloudQueue inputQueue = _fixture.QueueClient.GetQueueReference("samples-powershell");
            inputQueue.CreateIfNotExists();

            string id = Guid.NewGuid().ToString();
            JObject jsonObject = new JObject
            {
                { "id", id }
            };
            var message = new CloudQueueMessage(jsonObject.ToString(Formatting.None));
            await inputQueue.AddMessageAsync(message);

            // wait for function to execute and produce its result entity
            CloudTable table = _fixture.TableClient.GetTableReference("samples");
            TableOperation operation = TableOperation.Retrieve("samples-powershell", id);
            TableResult result = null;
            await TestHelpers.Await(() =>
            {
                result = table.Execute(operation);
                return result != null && result.HttpStatusCode == 200;
            });

            DynamicTableEntity entity = (DynamicTableEntity)result.Result;
            Assert.Equal(2, entity.Properties.Count);
            string title = entity.Properties["Title"].StringValue;
            Assert.Equal(string.Format("PowerShell Table Entity for message {0}", id), title);
        }

        [Fact]
        public async Task QueueTriggerPython_Succeeds()
        {
            TestHelpers.ClearFunctionLogs("QueueTrigger-Python");

            // write the input message
            CloudQueue inputQueue = _fixture.QueueClient.GetQueueReference("samples-python");
            inputQueue.CreateIfNotExists();

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
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            jsonObject = JObject.Parse(result);
            Assert.Equal(id, (string)jsonObject["id"]);

            // verify the function output
            var logs = await TestHelpers.GetFunctionLogsAsync("QueueTrigger-Python");

            // strip off the timestamps from the beginning of each line
            logs = logs.Select(l => l.Split(new[] { ' ' }, 2)[1]).ToList();
            int idx = logs.IndexOf("Read 5 Table entities");
            for (int i = idx + 1; i < 5; i++)
            {
                string json = logs[i];
                JObject entity = JObject.Parse(json);
                Assert.Equal("samples-python", entity["PartitionKey"]);
                Assert.Equal(0, (int)entity["Status"]);
            }
        }

        [Fact]
        public async Task BlobTriggerBatch_Succeeds()
        {
            // write input blob
            CloudBlobContainer inputContainer = _fixture.BlobClient.GetContainerReference("samples-batch");
            await inputContainer.CreateIfNotExistsAsync();

            // Processing a large number of blobs on startup can take a while,
            // so let's start with an empty container.
            TestHelpers.ClearContainer(inputContainer);

            string blobName = Guid.NewGuid().ToString();
            string testData = "This is a test";
            CloudBlockBlob inputBlob = inputContainer.GetBlockBlobReference(blobName);
            await inputBlob.UploadTextAsync(testData);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(blobName);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            // verify results
            Assert.Equal(testData, result.Trim());
        }

        [Fact]
        public async Task ServiceBusQueueTrigger_Succeeds()
        {
            string queueName = "samples-input";
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);
            namespaceManager.DeleteQueue(queueName);
            namespaceManager.CreateQueue(queueName);

            var client = Microsoft.ServiceBus.Messaging.QueueClient.CreateFromConnectionString(connectionString, queueName);

            // write a start message to the queue to kick off the processing
            int max = 3;
            string id = Guid.NewGuid().ToString();
            JObject message = new JObject
            {
                { "count", 1 },
                { "max", max },
                { "id", id }
            };
            using (Stream stream = new MemoryStream())
            using (TextWriter writer = new StreamWriter(stream))
            {
                writer.Write(message.ToString());
                writer.Flush();
                stream.Position = 0;

                client.Send(new BrokeredMessage(stream) { ContentType = "text/plain" });
            }

            client.Close();

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            Assert.Equal(string.Format("{0} messages processed", max), result.Trim());
        }

        [Fact]
        public async Task ServiceBusTopicTrigger_Succeeds()
        {
            string topicName = "samples-topic";
            string subscriptionName = "samples";
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            if (!namespaceManager.TopicExists(topicName))
            {
                namespaceManager.CreateTopic(topicName);
            }

            if (!namespaceManager.SubscriptionExists(topicName, subscriptionName))
            {
                namespaceManager.CreateSubscription(topicName, subscriptionName);
            }

            var client = Microsoft.ServiceBus.Messaging.TopicClient.CreateFromConnectionString(connectionString, topicName);

            // write a start message to the queue to kick off the processing
            string id = Guid.NewGuid().ToString();
            string value = Guid.NewGuid().ToString();
            JObject message = new JObject
            {
                { "id", id },
                { "value", value }
            };
            using (Stream stream = new MemoryStream())
            using (TextWriter writer = new StreamWriter(stream))
            {
                writer.Write(message.ToString());
                writer.Flush();
                stream.Position = 0;

                client.Send(new BrokeredMessage(stream) { ContentType = "text/plain" });
            }

            client.Close();

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            Assert.Equal(value, result.Trim());
        }

        [Fact]
        public async Task ServiceBusTopicTrigger_ManualInvoke_Succeeds()
        {
            string topicName = "samples-topic";
            string subscriptionName = "samples";
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            if (!namespaceManager.TopicExists(topicName))
            {
                namespaceManager.CreateTopic(topicName);
            }

            if (!namespaceManager.SubscriptionExists(topicName, subscriptionName))
            {
                namespaceManager.CreateSubscription(topicName, subscriptionName);
            }

            string uri = "admin/functions/servicebustopictrigger";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
            string id = Guid.NewGuid().ToString();
            string value = Guid.NewGuid().ToString();
            JObject input = new JObject()
            {
                {
                    "input", new JObject()
                    {
                        { "id", id },
                        { "value", value }
                    }.ToString()
                }
            };
            string json = input.ToString();
            request.Content = new StringContent(json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var client = Microsoft.ServiceBus.Messaging.TopicClient.CreateFromConnectionString(connectionString, topicName);

            // wait for function to execute and produce its result blob
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

            Assert.Equal(value, result.Trim());
        }

        [Fact]
        public async Task HostPing_Succeeds()
        {
            string uri = "admin/host/ping";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task HostLog_Anonymous_Fails()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz");
            var response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task HostLog_AdminLevel_Succeeds()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
            var logs = new HostLogEntry[]
            {
                new HostLogEntry
                {
                    Level = TraceLevel.Verbose,
                    Source = "ScaleController",
                    Message = string.Format("Test Verbose log {0}", Guid.NewGuid().ToString())
                },
                new HostLogEntry
                {
                    Level = TraceLevel.Info,
                    Source = "ScaleController",
                    Message = string.Format("Test Info log {0}", Guid.NewGuid().ToString())
                },
                new HostLogEntry
                {
                    Level = TraceLevel.Warning,
                    Source = "ScaleController",
                    Message = string.Format("Test Warning log {0}", Guid.NewGuid().ToString())
                },
                new HostLogEntry
                {
                    Level = TraceLevel.Error,
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

            var response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await Task.Delay(1000);

            var hostLogs = await TestHelpers.GetHostLogsAsync();
            foreach (var expectedLog in logs.Select(p => p.Message))
            {
                Assert.Equal(1, hostLogs.Count(p => p.Contains(expectedLog)));
            }
        }

        [Fact]
        public async Task HostStatus_AdminLevel_Succeeds()
        {
            string uri = "admin/host/status";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);

            HttpResponseMessage response = await GetHostStatusAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            JObject jsonContent = JObject.Parse(content);

            Assert.Equal(3, jsonContent.Properties().Count());
            AssemblyFileVersionAttribute fileVersionAttr = typeof(HostStatus).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            string expectedVersion = fileVersionAttr.Version;
            Assert.True(((string)jsonContent["id"]).Length > 0);
            Assert.Equal(expectedVersion, jsonContent["version"].ToString());
            var state = (string)jsonContent["state"];
            Assert.True(state == "Running" || state == "Created");

            // Now ensure XML content works
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
            request.Headers.Add("Accept", "text/xml");

            response = await this._fixture.HttpClient.SendAsync(request);
            content = await response.Content.ReadAsStringAsync();

            string ns = "http://schemas.datacontract.org/2004/07/Microsoft.Azure.WebJobs.Script.WebHost.Models";
            XDocument doc = XDocument.Parse(content);
            var node = doc.Descendants(XName.Get("Version", ns)).Single();
            Assert.Equal(expectedVersion, node.Value);
            node = doc.Descendants(XName.Get("Id", ns)).Single();
            Assert.True(node.Value.Length > 0);
            node = doc.Descendants(XName.Get("State", ns)).Single();
            Assert.True(node.Value == "Running" || node.Value == "Created");

            node = doc.Descendants(XName.Get("Errors", ns)).Single();
            Assert.True(node.IsEmpty);
        }

        [Fact]
        public async Task HostStatus_AnonymousAntaresInteral_CheckLoad_Succeeds()
        {
            string uri = "admin/host/status?checkLoad=1";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" },
                { EnvironmentSettingNames.AzureWebsiteAppCountersName, "{\"connections\": 290, \"connectionLimit\": 300}" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string content = await response.Content.ReadAsStringAsync();
                var jsonContent = JObject.Parse(content);

                Assert.Equal(4, jsonContent.Properties().Count());
                AssemblyFileVersionAttribute fileVersionAttr = typeof(HostStatus).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                string expectedVersion = fileVersionAttr.Version;
                Assert.True(((string)jsonContent["id"]).Length > 0);
                Assert.Equal(expectedVersion, jsonContent["version"].ToString());
                var state = (string)jsonContent["state"];
                Assert.True(state == "Running" || state == "Created");
                JObject loadStatus = (JObject)jsonContent["load"];
                Assert.True((bool)loadStatus["isHigh"]);
            }
        }

        [Fact]
        public async Task HostStatus_AnonymousLevelRequest_Fails()
        {
            string uri = "admin/host/status";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await this._fixture.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task HostStatus_AdminLevelRequest_WithLoad_Succeeds()
        {
            try
            {
                var counters = new JObject
                {
                    { "connections", 289 },
                    { "connectionLimit", 300 }
                };
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAppCountersName, counters.ToString());

                string uri = "admin/host/status?checkLoad=1";
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);
                var response = await this._fixture.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                string json = await response.Content.ReadAsStringAsync();
                JObject jsonContent = JObject.Parse(json);
                Assert.Equal(4, jsonContent.Properties().Count());
                JObject loadStatus = (JObject)jsonContent["load"];
                Assert.True((bool)loadStatus["isHigh"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAppCountersName, string.Empty);
            }
        }

        private async Task<HttpResponseMessage> GetHostStatusAsync()
        {
            string uri = "admin/host/status";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthorizationLevelAttribute.FunctionsKeyHeaderName, MasterKey);

            return await this._fixture.HttpClient.SendAsync(request);
        }

        public class TestFixture : IDisposable
        {
            private readonly ScriptSettingsManager _settingsManager;
            private HttpConfiguration _config;

            public TestFixture()
            {
                _config = new HttpConfiguration();
                _config.Formatters.Add(new PlaintextMediaTypeFormatter());

                _settingsManager = ScriptSettingsManager.Instance;
                HostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\sample"),
                    LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                    SecretsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\src\WebJobs.Script.WebHost\App_Data\Secrets")
                };
                WebApiConfig.Register(_config, _settingsManager, HostSettings);

                HttpServer = new HttpServer(_config);
                HttpClient = new HttpClient(HttpServer);
                HttpClient.BaseAddress = new Uri("https://localhost/");

                string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString("Storage");
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                BlobClient = storageAccount.CreateCloudBlobClient();
                QueueClient = storageAccount.CreateCloudQueueClient();
                TableClient = storageAccount.CreateCloudTableClient();

                var table = TableClient.GetTableReference("samples");
                table.CreateIfNotExists();

                var batch = new TableBatchOperation();
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "1", Title = "Test Entity 1", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "2", Title = "Test Entity 2", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "3", Title = "Test Entity 3", Status = 1 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "4", Title = "Test Entity 4", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "5", Title = "Test Entity 5", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "6", Title = "Test Entity 6", Status = 0 });
                batch.InsertOrReplace(new TestEntity { PartitionKey = "samples-python", RowKey = "7", Title = "Test Entity 7", Status = 0 });
                table.ExecuteBatch(batch);

                connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
                NamespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

                TestHelpers.WaitForWebHost(HttpClient);
            }

            public WebHostSettings HostSettings { get; private set; }

            public CloudTableClient TableClient { get; set; }

            public CloudBlobClient BlobClient { get; set; }

            public CloudQueueClient QueueClient { get; set; }

            public NamespaceManager NamespaceManager { get; set; }

            public HttpClient HttpClient { get; set; }

            public HttpServer HttpServer { get; set; }

            public void Dispose()
            {
                HttpServer?.Dispose();
                HttpClient?.Dispose();

                TestHelpers.ClearHostLogs();
            }

            private class TestEntity : TableEntity
            {
                public string Title { get; set; }

                public int Status { get; set; }
            }
        }
    }
}
#endif