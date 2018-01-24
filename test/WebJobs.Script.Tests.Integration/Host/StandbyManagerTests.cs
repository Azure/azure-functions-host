﻿// Copyright (c) .NET Foundation. All rights reserved.
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
            WebScriptHostManager.ResetStandbyMode();
        }

        [Fact]
        public void IsWarmUpRequest_ReturnsExpectedValue()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/warmup");
            Assert.False(StandbyManager.IsWarmUpRequest(request), "Initial request to 'warmup'.");

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, null }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(StandbyManager.IsWarmUpRequest(request), "Set PlaceholderMode to '1'.");

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
                Assert.True(StandbyManager.IsWarmUpRequest(request), "Set InstanceId to '12345'.");

                request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/csharphttpwarmup");
                Assert.True(StandbyManager.IsWarmUpRequest(request), "Request to 'csharpwarmup'.");

                request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/warmup");
                request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
                Assert.False(StandbyManager.IsWarmUpRequest(request), "Request to 'warmup'.");

                request = new HttpRequestMessage(HttpMethod.Post, "http://azure.com/api/foo");
                Assert.False(StandbyManager.IsWarmUpRequest(request), "Request to 'foo'.");
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
                await FileUtility.DeleteDirectoryAsync(testRootPath, true);
                var traceWriter = new TestTraceWriter(TraceLevel.Info);
                var webHostSettings = new WebHostSettings
                {
                    IsSelfHost = true,
                    LogPath = Path.Combine(testRootPath, "Logs"),
                    SecretsPath = Path.Combine(testRootPath, "Secrets"),
                    ScriptPath = Path.Combine(testRootPath, "WWWRoot"),
                    TraceWriter = traceWriter
                };
                WebApiConfig.Register(httpConfig, _settingsManager, webHostSettings);

                var httpServer = new HttpServer(httpConfig);
                var httpClient = new HttpClient(httpServer);
                httpClient.BaseAddress = new Uri("https://localhost/");

                TestHelpers.WaitForWebHost(httpClient);

                var traces = traceWriter.Traces.ToArray();
                Assert.Equal($"Creating StandbyMode placeholder function directory ({Path.GetTempPath()}Functions\\Standby\\WWWRoot)", traces[0].Message);
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

                // Now specialize the host
                ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                // give time for the specialization to happen
                await Task.Delay(2000);

                httpServer.Dispose();
                httpClient.Dispose();

                var hostConfig = WebHostResolver.CreateScriptHostConfiguration(webHostSettings, true);
                var expectedHostId = hostConfig.HostConfig.HostId;

                string[] logLines = traceWriter.Traces.Select(p => p.Message).ToArray();
                string text = string.Join(Environment.NewLine, logLines);
                Assert.True(logLines.Count(p => p.Contains("Stopping Host")) > 1);
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
    }
}
