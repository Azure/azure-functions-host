// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerTests
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
            ScriptSettingsManager.Instance.Reset();
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
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
        }

        [Fact(Skip = "Investigate test failure")]
        public async Task StandbyMode_EndToEnd()
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "87654639876900123453445678890144" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var httpConfig = new HttpConfiguration();

                var settingsManager = ScriptSettingsManager.Instance;
                var testRootPath = Path.Combine(Path.GetTempPath(), "StandbyModeTest");
                await FileUtility.DeleteDirectoryAsync(testRootPath, true);
                TestLoggerProvider loggerProvider = new TestLoggerProvider();
                var webHostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    LogPath = Path.Combine(testRootPath, "Logs"),
                    SecretsPath = Path.Combine(testRootPath, "Secrets"),
                    ScriptPath = Path.Combine(testRootPath, "WWWRoot"),
                };

                var httpServer = new HttpServer(httpConfig);
                var httpClient = new HttpClient(httpServer);
                httpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(httpClient);

                var traces = loggerProvider.GetAllLogMessages().ToArray();
                Assert.Equal($"Creating StandbyMode placeholder function directory ({Path.GetTempPath()}Functions\\Standby\\WWWRoot)", traces[0].FormattedMessage);
                Assert.Equal("StandbyMode placeholder function directory created", traces[1].FormattedMessage);

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
                request = new HttpRequestMessage(HttpMethod.Get, "api/dne");
                response = await httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

                httpServer.Dispose();
                httpClient.Dispose();

                await Task.Delay(3000);

                // verify logs
                string[] logLines = loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                int stopCount = logLines.Count(p => p.Contains("Stopping Host"));
                Assert.True(stopCount >= 1);
                Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));

                WebScriptHostManager.ResetStandbyMode();
            }
        }
    }
}
