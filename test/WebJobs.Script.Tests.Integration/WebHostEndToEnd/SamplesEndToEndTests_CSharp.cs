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
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.SamplesEndToEnd)]
    public class SamplesEndToEndTests_CSharp : IClassFixture<SamplesEndToEndTests_CSharp.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public SamplesEndToEndTests_CSharp(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }


        [Fact]
        public async Task ExtensionWebHook_Succeeds()
        {
            // configure a mock webhook handler for the "test" extension
            Mock<IAsyncConverter<HttpRequestMessage, HttpResponseMessage>> mockHandler = new Mock<IAsyncConverter<HttpRequestMessage, HttpResponseMessage>>(MockBehavior.Strict);
            mockHandler.Setup(p => p.ConvertAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
            var handler = mockHandler.Object;
            _fixture.MockWebHookProvider.Setup(p => p.TryGetHandler("test", out handler)).Returns(true);
            _fixture.MockWebHookProvider.Setup(p => p.TryGetHandler("invalid", out handler)).Returns(false);

            // successful request
            string uri = "runtime/webhooks/test?code=SystemValue3";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // invalid system key value - no key match
            uri = "runtime/webhooks/test?code=invalid";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // invalid system key value - wrong key match (must match key name for extension)
            uri = "runtime/webhooks/test?code=SystemValue2";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // verify admin requests are allowed through
            uri = "runtime/webhooks/test?code=1234";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // non-existent extension
            uri = "runtime/webhooks/invalid?code=SystemValue2";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        public async Task HostPing_Succeeds(string method)
        {
            string uri = "admin/host/ping";
            HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cacheHeader = response.Headers.GetValues("Cache-Control").Single();
            Assert.Equal("no-store, no-cache", cacheHeader);
        }

        [Fact]
        public async Task SyncTriggers_InternalAuth_Succeeds()
        {
            using (new TestScopedSettings(_settingsManager, EnvironmentSettingNames.AzureWebsiteInstanceId, "testinstance"))
            {
                string uri = "admin/host/synctriggers";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Fact]
        public async Task SyncTriggers_ExternalUnauthorized_ReturnsUnauthorized()
        {
            string uri = "admin/host/synctriggers";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task SyncTriggers_AdminLevel_Succeeds()
        {
            string uri = "admin/host/synctriggers";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());
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

            var hostLogs = _fixture.Host.GetScriptHostLogMessages();
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

            Assert.Equal(5, jsonContent.Properties().Count());
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

        [Fact]
        public async Task SetHostState_Offline_Succeeds()
        {
            string functionName = "HttpTrigger";

            // verify host is up and running
            var response = await GetHostStatusAsync();
            var hostStatus = response.Content.ReadAsAsync<HostStatus>();

            // verify functions can be invoked
            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, functionName);

            // verify function status is ok
            response = await GetFunctionStatusAsync(functionName);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var functionStatus = await response.Content.ReadAsAsync<FunctionStatus>();
            Assert.Null(functionStatus.Errors);

            // take host offline
            await SetHostStateAsync("offline");

            // when testing taking the host offline doesn't seem to stop all
            // application services, so we issue a restart
            await RestartHostAsync();

            // wait for the host to go offline
            await AwaitHostStateAsync(ScriptHostState.Offline);

            // verify function status returns 503 immediately
            await TestHelpers.RunWithTimeoutAsync(async () =>
            {
                response = await GetFunctionStatusAsync(functionName);
            }, TimeSpan.FromSeconds(1));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(functionStatus.Errors);

            // verify that when offline function requests return 503
            response = await SamplesTestHelpers.InvokeHttpTrigger(_fixture, functionName);
            await VerifyOfflineResponse(response);

            // verify that the root returns 503 immediately when offline
            var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            await VerifyOfflineResponse(response);

            // verify the same thing when invoking via admin api
            response = await AdminInvokeFunction(functionName);
            await VerifyOfflineResponse(response);

            // bring host back online
            await SetHostStateAsync("running");

            await AwaitHostStateAsync(ScriptHostState.Running);

            // need to reinitialize TestFunctionHost to reset IApplicationLifetime
            await _fixture.InitializeAsync();

            // verify functions can be invoked
            await SamplesTestHelpers.InvokeAndValidateHttpTrigger(_fixture, functionName);

            // verify the same thing via admin api
            response = await AdminInvokeFunction(functionName);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
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
        public async Task ListFunctions_Succeeds()
        {
            string uri = "admin/functions";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, await _fixture.Host.GetMasterKeyAsync());

            var response = await _fixture.Host.HttpClient.SendAsync(request);
            var metadata = (await response.Content.ReadAsAsync<IEnumerable<FunctionMetadataResponse>>()).ToArray();

            Assert.Equal(16, metadata.Length);
            var function = metadata.Single(p => p.Name == "HttpTrigger-CustomRoute");
            Assert.Equal("https://localhost/api/csharp/products/{category:alpha?}/{id:int?}/{extra?}", function.InvokeUrlTemplate.ToString());

            function = metadata.Single(p => p.Name == "HttpTrigger");
            Assert.Equal("https://localhost/api/httptrigger", function.InvokeUrlTemplate.ToString());
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
        public async Task ManualTrigger_Invoke_Succeeds()
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

                await _fixture.Host.BeginFunctionAsync("manualtrigger", input);

                // wait for completion
                CloudBlockBlob outputBlob = outputContainer.GetBlockBlobReference(outId);
                string result = await TestHelpers.WaitForBlobAndGetStringAsync(outputBlob);
                Assert.Equal("Hello C#!", Utility.RemoveUtf8ByteOrderMark(result));
            }
        }


        [Fact]
        public async Task HttpTrigger_Poco_Post_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-POCO");

                string uri = $"api/httptrigger-poco?code={functionKey}";
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
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-Compat");
                string id = Guid.NewGuid().ToString();
                string uri = $"api/HttpTrigger-Compat?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsAsync<string>();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("Hello from HttpResponseMessage", responseContent);
            }
        }

        [Fact]
        public async Task HttpTrigger_Poco_Get_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-Poco");
                string id = Guid.NewGuid().ToString();
                string uri = $"api/httptrigger-poco?code={functionKey}&Id={id}&Value=Testing";
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

        // invoke a function via the admin invoke api
        private async Task<HttpResponseMessage> AdminInvokeFunction(string functionName, string input = null)
        {
            string masterKey = await _fixture.Host.GetMasterKeyAsync();
            string uri = $"admin/functions/{functionName}?code={masterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            JObject jo = new JObject
            {
                { "input", input }
            };
            request.Content = new StringContent(jo.ToString());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return await _fixture.Host.HttpClient.SendAsync(request);
        }

        [Fact]
        public async Task HttpTrigger_DuplicateQueryParams_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                Environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName);
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
        }

        [Fact]
        public async Task HttpTrigger_CustomRoute_ReturnsExpectedResponse()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionName = "HttpTrigger-CustomRoute";
                string functionKey = await _fixture.Host.GetFunctionSecretAsync(functionName);
                string uri = $"api/csharp/products/electronics/123/extra?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string json = await response.Content.ReadAsStringAsync();
                var product = JObject.Parse(json);
                Assert.Equal("electronics", (string)product["category"]);
                Assert.Equal(123, (int?)product["id"]);
                var logs = _fixture.Host.GetScriptHostLogMessages("Function.HttpTrigger-CustomRoute.User");
                Assert.Contains(logs, l => string.Equals(l.FormattedMessage, "Parameters: category=electronics id=123 extra=extra"));
                Assert.True(logs.Any(p => p.FormattedMessage.Contains("ProductInfo: Category=electronics Id=123")));

                // test optional id parameter
                // test optional extra parameter (not in POCO binding contract)
                _fixture.Host.ClearLogMessages();
                uri = $"api/csharp/products/electronics?code={functionKey}";
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                json = await response.Content.ReadAsStringAsync();
                product = JObject.Parse(json);
                Assert.Equal("electronics", (string)product["category"]);
                Assert.Null((int?)product["id"]);
                logs = _fixture.Host.GetScriptHostLogMessages("Function.HttpTrigger-CustomRoute.User");
                Assert.Contains(logs, l => string.Equals(l.FormattedMessage, "Parameters: category=electronics id= extra="));

                // test optional category parameter
                _fixture.Host.ClearLogMessages();
                uri = $"api/csharp/products?code={functionKey}";
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                json = await response.Content.ReadAsStringAsync();
                product = JObject.Parse(json);
                Assert.Null((string)product["category"]);
                Assert.Null((int?)product["id"]);
                logs = _fixture.Host.GetScriptHostLogMessages("Function.HttpTrigger-CustomRoute.User");
                Assert.Contains(logs, l => string.Equals(l.FormattedMessage, "Parameters: category= id= extra="));

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
            }
        }

        [Fact]
        public async Task HttpTriggerWithObject_Post_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTriggerWithObject");

                string uri = $"api/httptriggerwithobject?code={functionKey}";
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

        [Fact]
        public async Task HttpTrigger_Identities_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName},
                { "WEBSITE_AUTH_ENABLED", "TRUE"}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-Identities");
                string uri = $"api/httptrigger-identities?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                MockEasyAuth(request, "facebook", "Connor McMahon", "10241897674253170");

                HttpResponseMessage response = await this._fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string responseContent = await response.Content.ReadAsStringAsync();
                string[] identityStrings = StripBookendQuotations(responseContent).Split(';');
                Assert.Equal("Identity: (facebook, Connor McMahon, 10241897674253170)", identityStrings[0]);
                Assert.Equal("Identity: (WebJobsAuthLevel, Function, Key1)", identityStrings[1]);
            }
        }

        [Fact]
        public async Task HttpTrigger_Identities_AnonymousAccessSucceeds()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName},
                { "WEBSITE_AUTH_ENABLED", "TRUE"}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string uri = $"api/httptrigger-identities";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                MockEasyAuth(request, "facebook", "Connor McMahon", "10241897674253170");

                HttpResponseMessage response = await this._fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string responseContent = await response.Content.ReadAsStringAsync();
                string[] identityStrings = StripBookendQuotations(responseContent).Split(';');
                Assert.Equal("Identity: (facebook, Connor McMahon, 10241897674253170)", identityStrings[0]);
            }
        }

        [Fact]
        public async Task HttpTrigger_Identities_BlocksSpoofedEasyAuthIdentity()
        {
            var vars = new Dictionary<string, string>
            {
                { LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.DotNetLanguageWorkerName},
                { "WEBSITE_AUTH_ENABLED", "FALSE"}
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger-Identities");
                string uri = $"api/httptrigger-identities?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                MockEasyAuth(request, "facebook", "Connor McMahon", "10241897674253170");

                HttpResponseMessage response = await this._fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string responseContent = await response.Content.ReadAsStringAsync();
                string identityString = StripBookendQuotations(responseContent);
                Assert.Equal("Identity: (WebJobsAuthLevel, Function, Key1)", identityString);
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

        private async Task<HttpResponseMessage> GetFunctionStatusAsync(string functionName)
        {
            var masterKey = await _fixture.Host.GetMasterKeyAsync();
            var request = new HttpRequestMessage(HttpMethod.Get, $"admin/functions/{functionName}/status");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);
            return await _fixture.Host.HttpClient.SendAsync(request);
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

        internal static string StripBookendQuotations(string response)
        {
            if (response.StartsWith("\"") && response.EndsWith("\""))
            {
                return response.Substring(1, response.Length - 2);
            }
            return response;
        }

        internal static void MockEasyAuth(HttpRequestMessage request, string provider, string name, string id)
        {
            string userIdentityJson = @"{
  ""auth_typ"": """ + provider + @""",
  ""claims"": [
    {
      ""typ"": ""http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"",
      ""val"": """ + name + @"""
    },
    {
      ""typ"": ""http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn"",
      ""val"": """ + name + @"""
    },
    {
      ""typ"": ""http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"",
      ""val"": """ + id + @"""
    }
  ],
  ""name_typ"": ""http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"",
  ""role_typ"": ""http://schemas.microsoft.com/ws/2008/06/identity/claims/role""
}";
            string easyAuthHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(userIdentityJson));
            request.Headers.Add("x-ms-client-principal", easyAuthHeaderValue);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample\csharp"), "samples", LanguageWorkerConstants.DotNetLanguageWorkerName)
            {
                MockWebHookProvider = new Mock<IScriptWebHookProvider>(MockBehavior.Strict);
            }

            public Mock<IScriptWebHookProvider> MockWebHookProvider { get; }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.Services.AddSingleton<IScriptWebHookProvider>(MockWebHookProvider.Object);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger",
                        "HttpTrigger-Compat",
                        "HttpTrigger-CustomRoute",
                        "HttpTrigger-POCO",
                        "HttpTrigger-Identities",
                        "HttpTriggerWithObject",
                        "ManualTrigger"
                    };
                });
            }
        }
    }
}