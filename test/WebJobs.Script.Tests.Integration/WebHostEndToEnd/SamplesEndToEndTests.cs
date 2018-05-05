// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(SamplesEndToEndTests))]
    public class SamplesEndToEndTests : IClassFixture<SamplesEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public object AuthorizationLevelAttribute { get; private set; }

        public SamplesEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
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
        public async Task ManualTrigger_CSharp_Invoke_Succeeds()
        {
            CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
            string inId = Guid.NewGuid().ToString();
            string outId = Guid.NewGuid().ToString();
            CloudBlockBlob statusBlob = outputContainer.GetBlockBlobReference(inId);
            await statusBlob.UploadTextAsync("Hello C#!");

            JObject input = new JObject()
            {
                { "InId", inId },
                { "OutId", outId }
            };

            await _fixture.Host.BeginFunctionAsync("manualtrigger-csharp", input);

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

        [Fact]
        public async Task HttpTrigger_CSharp_Poco_Post_Succeeds()
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-CSharp-POCO");

            string uri = $"api/httptrigger-csharp-poco?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);

            string id = Guid.NewGuid().ToString();
            JObject requestBody = new JObject
            {
                { "Id", id },
                { "Value", "Testing" }
            };
            request.Content = new StringContent(requestBody.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
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
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-CSharp-Poco");
            string id = Guid.NewGuid().ToString();
            string uri = $"api/httptrigger-csharp-poco?code={functionKey}&Id={id}&Value=Testing";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
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
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            string uri = $"api/httptrigger?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("Hello Mathew", body);

            // verify request also succeeds with master key
            string masterKey = await _fixture.Host.GetMasterKeyAsync();
            uri = $"api/httptrigger?code={masterKey}&name=Mathew";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact(Skip = "Node language worker isn't handling duplicate query params properly")]
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
            Assert.Equal("Hello Amy", body);
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

        [Fact(Skip = "Needs investigation")]
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

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri)
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
            await TestHelpers.Await(() =>
            {
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = _fixture.Host.HttpClient.SendAsync(request).GetAwaiter().GetResult();
                timestamp = response.Headers.GetValues("Shared-Module").First();
                return initialTimestamp != timestamp;
            }, timeout: 5000, pollingInterval: 1000);
            Assert.NotEqual(initialTimestamp, timestamp);

            initialTimestamp = timestamp;
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            timestamp = response.Headers.GetValues("Shared-Module").First();
            Assert.Equal(initialTimestamp, timestamp);
        }

        [Fact]
        public async Task HttpTrigger_CSharp_CustomRoute_ReturnsExpectedResponse()
        {
            string functionName = "HttpTrigger-CSharp-CustomRoute";
            string functionKey = await _fixture.Host.GetFunctionSecretAsync(functionName);
            string uri = $"api/csharp/products/electronics/123?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            var product = JObject.Parse(json);
            Assert.Equal("electronics", (string)product["Category"]);
            Assert.Equal(123, (int?)product["Id"]);

            // test optional id parameter
            uri = $"api/csharp/products/electronics?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            product = JObject.Parse(json);
            Assert.Equal("electronics", (string)product["Category"]);
            Assert.Null((int?)product["Id"]);

            // test optional category parameter
            uri = $"api/csharp/products?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            json = await response.Content.ReadAsStringAsync();
            product = JObject.Parse(json);
            Assert.Null((string)product["Category"]);
            Assert.Null((int?)product["Id"]);

            // test a constraint violation (invalid id)
            uri = $"api/csharp/products/electronics/1x3?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // test a constraint violation (invalid category)
            uri = $"api/csharp/products/999/123?code={functionKey}";
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // verify route parameters were part of binding data
            var logs = _fixture.Host.GetLogMessages(LogCategories.CreateFunctionUserCategory(functionName));
            Assert.True(logs.Any(p => p.FormattedMessage.Contains("Parameters: category=electronics id=123")));
            Assert.True(logs.Any(p => p.FormattedMessage.Contains("ProductInfo: Category=electronics Id=123")));
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

        [Fact(Skip = "Not currently supported.")]
        public async Task GenericWebHook_CSharp_Post_Succeeds()
        {
            string uri = "api/hooks/csharp/generic/test?code=827bdzxhqy3xc62cxa2hmfsh6gxzhg30s5pi64tu";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'Value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar Action: test", jsonObject["result"]);
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task GenericWebHook_CSharp_Dynamic_Post_Succeeds()
        {
            string uri = "api/webhook-generic-csharp-dynamic?code=88adV34UZhaydCLOjMzbMgUtXh3stBnL8LFcL9R17DL8HAY8PVhpZA==";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'Value': 'Foobar' }");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("\"Value: Foobar\"", body);
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task AzureWebHook_CSharp_Post_Succeeds()
        {
            string uri = "api/webhook-azure-csharp?code=yKjiimZjC1FQoGlaIj8TUfGltnPE/f2LhgZNq6Fw9/XfAOGHmSgUlQ==";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent(Integration.Properties.Resources.AzureWebHookEventRequest);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Unresolved", jsonObject["status"]);
        }

        [Fact]
        public async Task HttpTriggerWithObject_CSharp_Post_Succeeds()
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTriggerWithObject-CSharp");

            string uri = $"api/httptriggerwithobject-csharp?code={functionKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'SenderName': 'Fabio' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Hello, Fabio", jsonObject["Greeting"]);
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task GenericWebHook_Post_Succeeds()
        {
            string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a5";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task GenericWebHook_Post_AdminKey_Succeeds()
        {
            // Verify that sending the admin key bypasses WebHook auth
            string uri = "api/webhook-generic?code=t8laajal0a1ajkgzoqlfv5gxr4ebhqozebw4qzdy";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task GenericWebHook_Post_NamedKey_Succeeds()
        {
            // Authenticate using a named key (client id)
            string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a6&clientid=testclient";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent("{ 'value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact(Skip = "Not currently supported.")]
        public async Task GenericWebHook_Post_NamedKey_Headers_Succeeds()
        {
            // Authenticate using values specified via headers,
            // rather than URI query params
            string uri = "api/webhook-generic";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            HostSecretsInfo secrets = await _fixture.Host.SecretManager.GetHostSecretsAsync();
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, secrets.MasterKey);
            request.Headers.Add("x-functions-clientid", "testclient");
            request.Content = new StringContent("{ 'value': 'Foobar' }");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            JObject jsonObject = JObject.Parse(body);
            Assert.Equal("Value: Foobar", jsonObject["result"]);
        }

        [Fact(Skip = "Not currently supported.")]
        public void GenericWebHook_Post_NamedKeyInHeader_Succeeds()
        {
            // Authenticate using a named key (client id)
            //string uri = "api/webhook-generic?code=1388a6b0d05eca2237f10e4a4641260b0a08f3a6";
            //HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            //request.Headers.Add(WebHost.WebHooks.WebHookReceiverManager.FunctionsClientIdHeaderName, "testclient");
            //request.Content = new StringContent("{ 'value': 'Foobar' }");
            //request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            //HttpResponseMessage response = await this._fixture.HttpClient.SendAsync(request);
            //Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            //Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            //string body = await response.Content.ReadAsStringAsync();
            //JObject jsonObject = JObject.Parse(body);
            //Assert.Equal("Value: Foobar", jsonObject["result"]);
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
            TestHelpers.ClearHostLogs();

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

        public class TestFixture : EndToEndTestFixture
        {
            static TestFixture()
            {
                Environment.SetEnvironmentVariable("AzureWebJobs.HttpTrigger-Disabled.Disabled", "1");
            }

            public TestFixture() :
                base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples")
            {
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