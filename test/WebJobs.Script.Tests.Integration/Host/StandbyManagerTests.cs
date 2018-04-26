// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerTests : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;

        public StandbyManagerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            WebScriptHostManager.ResetStandbyMode();
        }

        [Fact]
        public void IsWarmUpRequest_ReturnsExpectedValue()
        {
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            Assert.False(StandbyManager.IsWarmUpRequest(request));

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, null }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // in this test we're forcing a transition from non-placeholder mode to placeholder mode
                // which can't happen in the wild, so we force a reset here
                WebScriptHostManager.ResetStandbyMode();

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(StandbyManager.IsWarmUpRequest(request));

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
                Assert.True(StandbyManager.IsWarmUpRequest(request));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/csharphttpwarmup");
                Assert.True(StandbyManager.IsWarmUpRequest(request));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
                request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
                Assert.False(StandbyManager.IsWarmUpRequest(request));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/foo");
                Assert.False(StandbyManager.IsWarmUpRequest(request));
            }

            vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, null }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                WebScriptHostManager.ResetStandbyMode();

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(StandbyManager.IsWarmUpRequest(request));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
                _settingsManager.SetSetting(EnvironmentSettingNames.ContainerName, "TestContainer");
                Assert.True(_settingsManager.IsLinuxContainerEnvironment);
                Assert.True(StandbyManager.IsWarmUpRequest(request));
            }
        }

        [Fact]
        public async Task StandbyMode_EndToEnd()
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "87654639876900123453445678890144" },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var httpConfig = new HttpConfiguration();

                var testRootPath = Path.Combine(Path.GetTempPath(), "StandbyModeTest");
                await FileUtility.DeleteDirectoryAsync(testRootPath, true);

                var loggerProvider = new TestLoggerProvider();
                var loggerProviderFactory = new TestLoggerProviderFactory(loggerProvider);
                var webHostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    LogPath = Path.Combine(testRootPath, "Logs"),
                    SecretsPath = Path.Combine(testRootPath, "Secrets"),
                    ScriptPath = Path.Combine(testRootPath, "WWWRoot")
                };

                var loggerFactory = new LoggerFactory();
                loggerFactory.AddProvider(loggerProvider);

                var webHostBuilder = Program.CreateWebHostBuilder()
                    .ConfigureServices(c => {
                        c.AddSingleton(webHostSettings)
                        .AddSingleton<ILoggerProviderFactory>(loggerProviderFactory)
                        .AddSingleton<ILoggerFactory>(loggerFactory);
                        });

                var httpServer = new TestServer(webHostBuilder);
                var httpClient = httpServer.CreateClient();
                httpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(httpClient);

                var traces = loggerProvider.GetAllLogMessages().ToArray();
                Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Starting Host (HostId=placeholder-host")));
                Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Host is in standby mode")));

                // issue warmup request and verify
                var request = new HttpRequestMessage(HttpMethod.Get, "api/warmup");
                var response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string responseBody = await response.Content.ReadAsStringAsync();
                Assert.Equal("WarmUp complete.", responseBody);

                // issue warmup request with restart and verify
                request = new HttpRequestMessage(HttpMethod.Get, "api/warmup?restart=1");
                response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                responseBody = await response.Content.ReadAsStringAsync();
                Assert.Equal("WarmUp complete.", responseBody);

                // Now specialize the host
                ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");

                Assert.False(WebScriptHostManager.InStandbyMode);
                Assert.True(ScriptSettingsManager.Instance.ContainerReady);

                // give time for the specialization to happen
                string[] logLines = null;
                await TestHelpers.Await(() =>
                {
                    // wait for the trace indicating that the host has been specialized
                    logLines = loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                    return logLines.Contains("Generating 0 job function(s)");
                }, userMessageCallback: () => string.Join(Environment.NewLine, loggerProvider.GetAllLogMessages().Select(p => $"[{p.Timestamp.ToString("HH:mm:ss.fff")}] {p.FormattedMessage}")));

                httpServer.Dispose();
                httpClient.Dispose();

                await Task.Delay(2000);

                var hostConfig = WebHostResolver.CreateScriptHostConfiguration(webHostSettings, true);
                var expectedHostId = hostConfig.HostConfig.HostId;

                // verify the rest of the expected logs
                string text = string.Join(Environment.NewLine, logLines);
                Assert.True(logLines.Count(p => p.Contains("Stopping Host")) >= 1);
                Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
                Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Starting Host (HostId=placeholder-host")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
                Assert.Equal(1, logLines.Count(p => p.Contains($"Starting Host (HostId={expectedHostId}")));
                Assert.Contains("Generating 0 job function(s)", logLines);

                WebScriptHostManager.ResetStandbyMode();
            }
        }

        [Fact]
        public async Task StandbyMode_EndToEnd_LinuxContainer()
        {
            byte[] bytes = TestHelpers.GenerateKeyBytes();
            var encryptionKey = Convert.ToBase64String(bytes);

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.ContainerName, "TestContainer" },
                { EnvironmentSettingNames.ContainerEncryptionKey, encryptionKey },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var httpConfig = new HttpConfiguration();

                var testRootPath = Path.Combine(Path.GetTempPath(), "StandbyModeTest_Linux");
                await FileUtility.DeleteDirectoryAsync(testRootPath, true);

                var loggerProvider = new TestLoggerProvider();
                var loggerProviderFactory = new TestLoggerProviderFactory(loggerProvider);
                var webHostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    LogPath = Path.Combine(testRootPath, "Logs"),
                    SecretsPath = Path.Combine(testRootPath, "Secrets"),
                    ScriptPath = Path.Combine(testRootPath, "WWWRoot")
                };

                var loggerFactory = new LoggerFactory();
                loggerFactory.AddProvider(loggerProvider);

                var webHostBuilder = Program.CreateWebHostBuilder()
                    .ConfigureServices(c =>
                    {
                        c.AddSingleton(webHostSettings)
                        .AddSingleton<ILoggerProviderFactory>(loggerProviderFactory)
                        .AddSingleton<ILoggerFactory>(loggerFactory);
                    });

                var httpServer = new TestServer(webHostBuilder);
                var httpClient = httpServer.CreateClient();
                httpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(httpClient);

                var traces = loggerProvider.GetAllLogMessages().ToArray();
                Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Starting Host (HostId=placeholder-host")));
                Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Host is in standby mode")));

                // issue warmup request and verify
                var request = new HttpRequestMessage(HttpMethod.Get, "api/warmup");
                var response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                string responseBody = await response.Content.ReadAsStringAsync();
                Assert.Equal("WarmUp complete.", responseBody);

                // issue warmup request with restart and verify
                request = new HttpRequestMessage(HttpMethod.Get, "api/warmup?restart=1");
                response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                responseBody = await response.Content.ReadAsStringAsync();
                Assert.Equal("WarmUp complete.", responseBody);

                // Now specialize the host by invoking assign
                var secretManager = httpServer.Host.Services.GetService<ISecretManager>();
                var masterKey = (await secretManager.GetHostSecretsAsync()).MasterKey;
                string uri = "admin/instance/assign";
                request = new HttpRequestMessage(HttpMethod.Post, uri);
                var environment = new Dictionary<string, string>();
                var assignmentContext = new HostAssignmentContext
                {
                    SiteId = 1234,
                    SiteName = "TestSite",
                    Environment = environment
                };
                var encryptedAssignmentContext = EncryptedHostAssignmentContext.Create(assignmentContext, encryptionKey);
                string json = JsonConvert.SerializeObject(encryptedAssignmentContext);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);
                response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

                // give time for the specialization to happen
                string[] logLines = null;
                await TestHelpers.Await(() =>
                {
                    // wait for the trace indicating that the host has been specialized
                    logLines = loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                    return logLines.Contains("Generating 0 job function(s)");
                }, userMessageCallback: () => string.Join(Environment.NewLine, loggerProvider.GetAllLogMessages().Select(p => $"[{p.Timestamp.ToString("HH:mm:ss.fff")}] {p.FormattedMessage}")));

                httpServer.Dispose();
                httpClient.Dispose();

                await Task.Delay(2000);

                var hostConfig = WebHostResolver.CreateScriptHostConfiguration(webHostSettings, true);
                var expectedHostId = hostConfig.HostConfig.HostId;

                // verify the rest of the expected logs
                string text = string.Join(Environment.NewLine, logLines);
                Assert.True(logLines.Count(p => p.Contains("Stopping Host")) >= 1);
                Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
                Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Starting Host (HostId=placeholder-host")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
                Assert.Equal(1, logLines.Count(p => p.Contains($"Starting Host (HostId={expectedHostId}")));
                Assert.Contains("Generating 0 job function(s)", logLines);

                WebScriptHostManager.ResetStandbyMode();
            }
        }

        public void Dispose()
        {
        }
    }
}
