// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerTests
    {
        private readonly TestTraceWriter _traceWriter;
        private readonly ScriptSettingsManager _settingsManager;

        public StandbyManagerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _traceWriter = new TestTraceWriter(TraceLevel.Info);
        }

        [Fact]
        public void IsWarmUpRequest_ReturnsExpectedValue()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/warmup");
            Assert.False(StandbyManager.IsWarmUpRequest(request));

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, null }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(StandbyManager.IsWarmUpRequest(request));

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
                Assert.True(StandbyManager.IsWarmUpRequest(request));

                request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/csharphttpwarmup");
                Assert.True(StandbyManager.IsWarmUpRequest(request));

                request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/warmup");
                request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
                Assert.False(StandbyManager.IsWarmUpRequest(request));

                request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/foo");
                Assert.False(StandbyManager.IsWarmUpRequest(request));
            }
        }

        [Fact]
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
                httpConfig.Formatters.Add(new PlaintextMediaTypeFormatter());

                var settingsManager = ScriptSettingsManager.Instance;
                var testRootPath = Path.Combine(Path.GetTempPath(), "StandbyModeTest");
                if (Directory.Exists(testRootPath))
                {
                    Directory.Delete(testRootPath, true);
                }
                var traceWriter = new TestTraceWriter(TraceLevel.Info);
                var webHostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    LogPath = Path.Combine(testRootPath, "Logs"),
                    SecretsPath = Path.Combine(testRootPath, "Secrets"),
                    TraceWriter = traceWriter
                };
                WebApiConfig.Register(httpConfig, _settingsManager, webHostSettings);

                var httpServer = new HttpServer(httpConfig);
                var httpClient = new HttpClient(httpServer);
                httpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(httpClient);

                var traces = traceWriter.Traces.ToArray();
                Assert.Equal($"Creating StandbyMode placeholder function directory ({Path.GetTempPath()}Functions\\Standby)", traces[0].Message);
                Assert.Equal("StandbyMode placeholder function directory created", traces[1].Message);

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

                httpServer.Dispose();
                httpClient.Dispose();

                await Task.Delay(2000);

                // verify host logs
                string hostLogDirectory = Path.Combine(webHostSettings.LogPath, "Host");
                string hostLogFilePath = Directory.EnumerateFiles(hostLogDirectory).First();
                string[] logLines = File.ReadAllLines(hostLogFilePath);
                Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Stopping Host")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
            }
        }
    }
}
