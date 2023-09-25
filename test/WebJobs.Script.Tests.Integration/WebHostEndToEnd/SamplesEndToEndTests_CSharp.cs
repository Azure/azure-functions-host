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
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
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
        public async Task HostAdminApis_ValidAdminToken_Succeeds()
        {
            // verify with SWT
            string uri = "admin/host/status";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(1));
            request.Headers.Add(ScriptConstants.SiteRestrictedTokenHeaderName, token);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // verify with JWT
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            token = _fixture.Host.GenerateAdminJwtToken();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task InvokeAdminLevelFunction_WithoutMasterKey_ReturnsUnauthorized()
        {
            // no key presented
            string uri = $"api/httptrigger-adminlevel?name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // function level key when admin is required
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            string key = await _fixture.Host.GetFunctionSecretAsync("httptrigger-adminlevel");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, key);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // required master key supplied
            request = new HttpRequestMessage(HttpMethod.Get, uri);
            key = await _fixture.Host.GetMasterKeyAsync();
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, key);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // verify that even though a site token grants admin level access to
            // host APIs, it can't be used to invoke user functions
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2));

                // verify the token is valid by invoking an admin API
                request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");
                request.Headers.Add(ScriptConstants.SiteRestrictedTokenHeaderName, token);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                // verify it can't be used to invoke user functions
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add(ScriptConstants.SiteRestrictedTokenHeaderName, token);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        [Fact]
        public async Task ExtensionWebHook_Succeeds()
        {
            // configure a mock webhook handler for the "test" extension
            Mock<IAsyncConverter<HttpRequestMessage, HttpResponseMessage>> mockHandler = new Mock<IAsyncConverter<HttpRequestMessage, HttpResponseMessage>>(MockBehavior.Strict);
            mockHandler.Setup(p => p.ConvertAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK));
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
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            // verify admin requests are allowed through
            uri = "runtime/webhooks/test?code=1234";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // non-existent extension
            uri = "runtime/webhooks/invalid?code=SystemValue2";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [Trait(TestTraits.Group, TestTraits.AdminIsolationTests)]
        [InlineData("admin/host/status", true, true, false, false, true, HttpStatusCode.Forbidden)]
        [InlineData("admin/host/status", true, true, true, false, false, HttpStatusCode.Unauthorized)]
        [InlineData("admin/host/status", true, true, true, true, true, HttpStatusCode.OK)]
        [InlineData("admin/host/status", true, false, false, false, true, HttpStatusCode.OK)]
        [InlineData("admin/host/status", true, false, false, true, true, HttpStatusCode.OK)]
        [InlineData("admin/host/status", true, true, true, false, true, HttpStatusCode.OK)]
        [InlineData("admin/host/status", false, true, false, true, true, HttpStatusCode.Forbidden)]
        [InlineData("admin/host/extensionBundle/v1/templates", true, true, false, true, false, HttpStatusCode.Unauthorized)]
        [InlineData("admin/host/extensionBundle/v1/templates", true, true, true, false, true, HttpStatusCode.NotFound)]
        [InlineData("admin/host/extensionBundle/v1/templates", true, false, false, false, true, HttpStatusCode.NotFound)]
        [InlineData("admin/host/extensionBundle/v1/templates", true, true, false, true, true, HttpStatusCode.Forbidden)]
        [InlineData("admin/vfs/host.json", true, true, true, false, true, HttpStatusCode.OK)]
        [InlineData("admin/vfs/host.json", true, true, false, false, true, HttpStatusCode.Unauthorized)]
        [InlineData("admin/vfs/host.json", true, true, true, false, false, HttpStatusCode.Unauthorized)]
        public async Task AdminIsolation_ReturnsExpectedStatus(string uri, bool isAppService, bool enableIsolation, bool isPlatformInternal, bool bypassFE, bool addAuthKey, HttpStatusCode expectedStatus)
        {
            var environment = this._fixture.Host.WebHostServices.GetService<IEnvironment>();
            string websiteInstanceId = environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId);

            try
            {
                if (enableIsolation)
                {
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsAdminIsolationEnabled, "1");
                    Assert.True(environment.IsAdminIsolationEnabled());
                }

                if (!isAppService)
                {
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, null);
                    Assert.False(environment.IsAppService());
                }
                else
                {
                    Assert.True(environment.IsAppService());
                }

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                
                if (addAuthKey)
                {
                    await this._fixture.AddMasterKey(request);
                }

                if (isPlatformInternal)
                {
                    request.Headers.Add(ScriptConstants.AntaresPlatformInternal, "True");
                }

                if (!bypassFE)
                {
                    request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, Guid.NewGuid().ToString());
                }

                using (var httpClient = _fixture.Host.CreateHttpClient())
                {
                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    Assert.Equal(expectedStatus, response.StatusCode);
                }
            }
            finally
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsAdminIsolationEnabled, null);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, websiteInstanceId);
            }
        }



        [Theory]
        [InlineData("admin/vfs/site/wwwroot/host.json", HttpStatusCode.OK)]
        [InlineData("admin/vfs/host.json", HttpStatusCode.Forbidden)]
        public async Task AccessPathOutsideHome_ReturnsExpectedStatus(string uri, HttpStatusCode expectedStatus)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            await this._fixture.AddMasterKey(request);

            string nowString = DateTime.UtcNow.ToString("yyMMdd-HHmmss");
            string home = Path.Combine(Path.GetTempPath(), nowString, "home");
            string wwwroot = Path.Combine(home, "site", "wwwroot");
            FileUtility.CopyDirectory(this._fixture.RootScriptPath, wwwroot);

            using var envVars = new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, home);
            {
                using (var httpClient = _fixture.Host.CreateHttpClient())
                {
                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    Assert.Equal(expectedStatus, response.StatusCode);
                }

            }
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
        public async Task InstallExtensionsEnsureOldPathReturns404()
        {
            ExtensionPackageReferenceWithActions body = new ExtensionPackageReferenceWithActions();
            var jsonBody = JsonConvert.SerializeObject(body);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, "123");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Content = content;
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task InstallExtensionTest()
        {
            ExtensionPackageReferenceWithActions body = new ExtensionPackageReferenceWithActions();
            var jsonBody = JsonConvert.SerializeObject(body);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, "admin/host/extensions");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Content = content;
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_NonExtensionRoute_Succeeds()
        {
            // when request not made via ARM extensions route, expect success
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/keys");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_GetSecrets_NonAdmin_Unauthorized()
        {
            // when GET request for secrets is made via ARM extensions route, expect unauthorized
            var request = new HttpRequestMessage(HttpMethod.Get, "admin/host/keys");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            Assert.Equal(Microsoft.Azure.WebJobs.Script.WebHost.Properties.Resources.UnauthorizedArmExtensionResourceRequest, content);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_GetSecrets_Admin_Succeeds()
        {
            // owner or co-admin always authorized
            var request = new HttpRequestMessage(HttpMethod.Get, "admin/host/keys");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            request.Headers.Add(ScriptConstants.AntaresClientAuthorizationSourceHeader, "Legacy");
            request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_GetSecrets_Internal_Succeeds()
        {
            // hostruntime requests made internally by Geo (not over hostruntime bridge) are not filtered
            var request = new HttpRequestMessage(HttpMethod.Get, "admin/host/keys");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_GetSecrets_NoKey_Unauthorized()
        {
            // without master key the request is unauthorized (before the filter is even run)
            var request = new HttpRequestMessage(HttpMethod.Get, "admin/host/keys");
            request.Headers.Add(ScriptConstants.AntaresClientAuthorizationSourceHeader, "Legacy");
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_InvalidKey_Unauthorized()
        {
            // with an invalid master key the request is unauthorized (before the filter is even run)
            var request = new HttpRequestMessage(HttpMethod.Get, "admin/host/keys");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, "invalid");
            request.Headers.Add(ScriptConstants.AntaresClientAuthorizationSourceHeader, "Legacy");
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_NonGet_Succeeds()
        {
            // if the extensions request is anything other than a GET, allow it
            var request = new HttpRequestMessage(HttpMethod.Delete, "admin/host/keys/dne");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "true");
            request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task ArmExtensionsResourceFilter_GetNonSecretResource_Succeeds()
        {
            // resources that don't return secrets aren't restricted
            var request = new HttpRequestMessage(HttpMethod.Get, "admin/host/ping");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, _fixture.MasterKey);
            request.Headers.Add(ScriptConstants.AntaresARMExtensionsRouteHeader, "1");
            request.Headers.Add(ScriptConstants.AntaresARMRequestTrackingIdHeader, "1234");
            var response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task SyncTriggers_InternalAuth_Succeeds()
        {
            using (var httpClient = _fixture.Host.CreateHttpClient())
            {
                string uri = "admin/host/synctriggers";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
                HttpResponseMessage response = await httpClient.SendAsync(request);
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
            request.Content = new StringContent("[]");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task HostLog_PlatformInternal_Succeeds()
        {
            var environment = _fixture.Host.JobHostServices.GetService<IEnvironment>();
            Assert.True(environment.IsAppService());

            using (var httpClient = _fixture.Host.CreateHttpClient())
            {
                // no x-arr-log-id header makes this request platform internal
                var request = new HttpRequestMessage(HttpMethod.Post, "admin/host/log");
                request.Content = new StringContent("[]");
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                var response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
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
            HttpResponseMessage response = await GetHostStatusAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string content = await response.Content.ReadAsStringAsync();
            JObject jsonContent = JObject.Parse(content);

            Assert.Equal(9, jsonContent.Properties().Count());
            AssemblyFileVersionAttribute fileVersionAttr = typeof(HostStatus).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            Assert.True(((string)jsonContent["id"]).Length > 0);
            string expectedVersion = fileVersionAttr.Version;
            Assert.Equal(expectedVersion, (string)jsonContent["version"]);
            string expectedVersionDetails = Utility.GetInformationalVersion(typeof(ScriptHost));
            Assert.Equal(expectedVersionDetails, (string)jsonContent["versionDetails"]);
            var state = (string)jsonContent["state"];
            Assert.True(state == "Running" || state == "Created" || state == "Initialized");
            Assert.Equal((string)jsonContent["instanceId"], "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b");
            Assert.Equal((string)jsonContent["platformVersion"], "89.0.7.73");
            Assert.Equal((string)jsonContent["computerName"], "RD281878FCB8E7");

            // Now ensure XML content works
            string uri = "admin/host/status";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
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

        [Fact(Skip = "Offline check conflicting with other tests, needs investigation")]
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
            await SamplesTestHelpers.SetHostStateAsync(_fixture, "offline");

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
            await SamplesTestHelpers.SetHostStateAsync(_fixture, "running");

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

            Assert.Equal(18, metadata.Length);
            var function = metadata.Single(p => p.Name == "HttpTrigger-CustomRoute");
            Assert.Equal("https://somewebsite.azurewebsites.net/api/csharp/products/{category:alpha?}/{id:int?}/{extra?}", function.InvokeUrlTemplate.ToString());

            function = metadata.Single(p => p.Name == "HttpTrigger");
            Assert.Equal("https://somewebsite.azurewebsites.net/api/httptrigger", function.InvokeUrlTemplate.ToString());
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
            var environment = _fixture.Host.JobHostServices.GetService<IEnvironment>();
            Assert.True(environment.IsAppService());

            // Pings to the site root should not return the homepage content if they are internal requests.
            // The sent request does NOT include an X-ARR-LOG-ID header. This indicates the request was internal.
            using (var httpClient = _fixture.Host.CreateHttpClient())
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
                HttpResponseMessage response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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

                string content = requestBody.ToString();
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                request.Content.Headers.ContentLength = content.Length;

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
        public async Task HttpTrigger_Get_WithoutContentType_Succeeds()
        {
            await SamplesTestHelpers.InvokeAndValidateHttpTriggerWithoutContentType(_fixture, "HttpTrigger");
        }

        [Fact]
        public async Task HttpTrigger_CorrelationIDsAreLogged()
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger");

            string requestId = Guid.NewGuid().ToString();

            _fixture.Host.ClearLogMessages();
            _fixture.EventGenerator.ClearEvents();

            string uri = $"api/HttpTrigger?code={functionKey}&name=Mathew";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, requestId);
            request.Headers.Add("User-Agent", new string[] { "TestAgent" });

            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var webHostLogs = _fixture.Host.GetWebHostLogMessages();
            var httpTraceLogs = webHostLogs.Where(p => p.Category == typeof(SystemTraceMiddleware).FullName).ToList();
            Assert.Equal(2, httpTraceLogs.Count);

            // validate executing trace
            var log = httpTraceLogs[0];
            Assert.Equal(typeof(SystemTraceMiddleware).FullName, log.Category);
            Assert.Equal(LogLevel.Information, log.Level);
            var idx = log.FormattedMessage.IndexOf(':');
            var message = log.FormattedMessage.Substring(0, idx).Trim();
            Assert.Equal("Executing HTTP request", message);
            var details = log.FormattedMessage.Substring(idx + 1).Trim();
            var jo = JObject.Parse(details);
            Assert.Equal(4, jo.Count);
            Assert.Equal(requestId, jo["requestId"]);
            Assert.Equal("GET", jo["method"]);
            Assert.Equal("/api/HttpTrigger", jo["uri"]);
            Assert.Equal("TestAgent", jo["userAgent"]);

            // validate executed trace
            log = httpTraceLogs[1];
            Assert.Equal(typeof(SystemTraceMiddleware).FullName, log.Category);
            Assert.Equal(LogLevel.Information, log.Level);
            idx = log.FormattedMessage.IndexOf(':');
            message = log.FormattedMessage.Substring(0, idx).Trim();
            Assert.Equal("Executed HTTP request", message);
            details = log.FormattedMessage.Substring(idx + 1).Trim();
            jo = JObject.Parse(details);
            Assert.Equal(4, jo.Count);
            Assert.Equal(requestId, (string)jo["requestId"]);
            Assert.Equal(200, jo["status"]);
            var duration = (long)jo["duration"];
            Assert.True(duration > 0);

            // determine the function invocation ID
            var scriptHostLogs = _fixture.Host.GetScriptHostLogMessages();
            var functionInvocationStartingLog = scriptHostLogs.Single(p => p.EventId.Name == "FunctionStarted");
            string functionInvocationId = (string)functionInvocationStartingLog.Scope[ScriptConstants.LogPropertyFunctionInvocationIdKey];

            // verify we've stamped the function invocation logs with the Activity ID (i.e. the Request ID)
            // and invocation ID
            var traceEvents = _fixture.EventGenerator.GetFunctionTraceEvents();
            var httpTraceEvents = traceEvents.Where(p => p.Source == "Function.HttpTrigger").ToArray();
            Assert.Equal(2, httpTraceEvents.Length);
            Assert.All(httpTraceEvents, p => Assert.Equal(requestId, p.ActivityId));
            Assert.All(httpTraceEvents, p => Assert.Equal(functionInvocationId, p.FunctionInvocationId));
        }

        [Fact]
        public async Task Legacy_RequestTypes_Succeed()
        {
            var vars = new Dictionary<string, string>
            {
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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
        [Trait(TestTraits.Group, TestTraits.AdminIsolationTests)]
        public async Task HttpTrigger_AdminLevel_AdminIsolationEnabled_Succeeds()
        {
            var environment = this._fixture.Host.WebHostServices.GetService<IEnvironment>();

            var vars = new Dictionary<string, string>
            {
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName},
                { EnvironmentSettingNames.FunctionsAdminIsolationEnabled, "1" }
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
            {
                Assert.True(environment.IsAdminIsolationEnabled());
                Assert.True(environment.IsAppService());

                // verify admin isolation checks don't apply to customer admin level functions,
                // only our admin APIs.

                // no key presented
                string uri = $"api/httptrigger-adminlevel?name=Mathew";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

                // function level key when admin is required
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                string key = await _fixture.Host.GetFunctionSecretAsync("httptrigger-adminlevel");
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, key);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

                // required master key supplied
                request = new HttpRequestMessage(HttpMethod.Get, uri);
                key = await _fixture.Host.GetMasterKeyAsync();
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, key);
                response = await _fixture.Host.HttpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        [Fact]
        public async Task HttpTrigger_DuplicateQueryParams_Succeeds()
        {
            var vars = new Dictionary<string, string>
            {
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
            {
                Environment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName);
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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
            {
                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTriggerWithObject");

                string uri = $"api/httptriggerwithobject?code={functionKey}";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
                string content = "{ 'SenderName': 'Fabio' }";
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                request.Content.Headers.ContentLength = content.Length;

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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName},
                { "WEBSITE_AUTH_ENABLED", "TRUE"}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName},
                { "WEBSITE_AUTH_ENABLED", "TRUE"}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
            {
                string uri = $"api/httptrigger-identities";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);

                MockEasyAuth(request, "facebook", "Connor McMahon", "10241897674253170");

                HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
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
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName},
                { "WEBSITE_AUTH_ENABLED", "FALSE"}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
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
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "csharp"), "samples", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
                MockWebHookProvider = new Mock<IScriptWebHookProvider>(MockBehavior.Strict);
            }

            public Mock<IScriptWebHookProvider> MockWebHookProvider { get; }

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

                // The legacy http tests use sync IO so explicitly allow this
                var environment = new TestEnvironment();
                string testSiteName = "somewebsite";
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagAllowSynchronousIO);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, testSiteName);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionWindows, "89.0.7.73");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresComputerName, "RD281878FCB8E7");

                // have to set these statically here because some APIs in the host aren't going through IEnvironment
                string key = TestHelpers.GenerateKeyHexString();
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, key);
                Environment.SetEnvironmentVariable("AzureWebEncryptionKey", key);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, testSiteName);

                services.AddSingleton<IEnvironment>(_ => environment);
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.Services.AddSingleton<IScriptWebHookProvider>(MockWebHookProvider.Object);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger",
                        "HttpTrigger-AdminLevel",
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