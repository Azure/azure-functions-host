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
    public class SamplesCSharpEndToEndTests : IClassFixture<SamplesCSharpEndToEndTests.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesCSharpEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        public object AuthorizationLevelAttribute { get; private set; }


        [Fact]
        public async Task ManualTrigger_CSharp_Invoke_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
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
        }


        [Fact]
        public async Task HttpTrigger_CSharp_Poco_Post_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
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

                request.Content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                // wait for function to execute and produce its result blob
                CloudBlobContainer outputContainer = _fixture.BlobClient.GetContainerReference("samples-output");
                CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(id);
                string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);

                Assert.Equal("Testing", TestHelpers.RemoveByteOrderMarkAndWhitespace(result));
            }
        }

        [Fact]
        public async Task Legacy_RequestTypes_Succeed()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-CSharp-Compat");
                string id = Guid.NewGuid().ToString();
                string uri = $"api/HttpTrigger-CSharp-Compat?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsAsync<string>();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("Hello from HttpResponseMessage", responseContent);
            }
        }

        [Fact]
        public async Task HttpTrigger_CSharp_Poco_Get_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
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
        public async Task HttpTrigger_CSharp_DuplicateQueryParams_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName);
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("httptrigger-csharp");
                string uri = $"api/httptrigger-csharp?code={functionKey}&name=Mathew&name=Amy";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string body = await response.Content.ReadAsStringAsync();
                Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
                Assert.Equal("Hello, Mathew,Amy", body);
            }
        }

        [Fact]
        public async Task HttpTrigger_CSharp_CustomRoute_ReturnsExpectedResponse()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionName = "HttpTrigger-CSharp-CustomRoute";
                string functionKey = await _fixture.Host.GetFunctionSecretAsync(functionName);
                string uri = $"api/csharp/products/electronics/123?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string json = await response.Content.ReadAsStringAsync();
                var product = JObject.Parse(json);
                Assert.Equal("electronics", (string)product["category"]);
                Assert.Equal(123, (int?)product["id"]);

                // test optional id parameter
                uri = $"api/csharp/products/electronics?code={functionKey}";
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                json = await response.Content.ReadAsStringAsync();
                product = JObject.Parse(json);
                Assert.Equal("electronics", (string)product["category"]);
                Assert.Null((int?)product["id"]);

                // test optional category parameter
                uri = $"api/csharp/products?code={functionKey}";
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                json = await response.Content.ReadAsStringAsync();
                product = JObject.Parse(json);
                Assert.Null((string)product["category"]);
                Assert.Null((int?)product["id"]);

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
        }
        
        [Fact]
        public async Task HttpTriggerWithObject_CSharp_Post_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
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
                Assert.Equal("Hello, Fabio", jsonObject["greeting"]);
            }
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
            }

            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"), "samples", LanguageWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            public override void ConfigureJobHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureJobHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger-CSharp",
                        "HttpTrigger-CSharp-Compat",
                        "HttpTrigger-CSharp-CustomRoute",
                        "HttpTrigger-CSharp-POCO",
                        "HttpTriggerWithObject-CSharp",
                        "ManualTrigger-CSharp"
                    };
                });
            }
        }
    }
}