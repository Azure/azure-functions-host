// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerE2ETestBase : IDisposable
    {
        protected readonly string _testRootPath;
        protected string _expectedHostId;
        protected TestLoggerProvider _loggerProvider;
        protected string _expectedScriptPath;
        protected HttpClient _httpClient;
        protected TestServer _httpServer;
        protected readonly object _originalTimeZoneInfoCache = GetCachedTimeZoneInfo();
        protected TestMetricsLogger _metricsLogger;

        public StandbyManagerE2ETestBase()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "StandbyManagerTests");
            CleanupTestDirectory();

            StandbyManager.ResetChangeToken();
        }

        protected async Task InitializeTestHostAsync(string testDirName, IEnvironment environment)
        {
            var httpConfig = new HttpConfiguration();
            var uniqueTestRootPath = Path.Combine(_testRootPath, testDirName, Guid.NewGuid().ToString());
            var scriptRootPath = Path.Combine(uniqueTestRootPath, "wwwroot");

            FileUtility.EnsureDirectoryExists(scriptRootPath);
            string proxyConfigPath = Path.Combine(scriptRootPath, "proxies.json");
            File.WriteAllText(proxyConfigPath, "{}");
            await TestHelpers.Await(() => File.Exists(proxyConfigPath));

            _loggerProvider = new TestLoggerProvider();
            _metricsLogger = new TestMetricsLogger();

            if (environment.IsAppServiceEnvironment())
            {
                // if the test is mocking App Service environment, we need
                // to also set the HOME and WEBSITE_SITE_NAME variables
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, uniqueTestRootPath);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "test-host-name");
            }

            var webHostBuilder = Program.CreateWebHostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    // This source reads from AzureWebJobsScriptRoot, which does not work
                    // with the custom paths that these tests are using.
                    var source = c.Sources.OfType<WebScriptHostConfigurationSource>().SingleOrDefault();
                    if (source != null)
                    {
                        c.Sources.Remove(source);
                    }
                    c.AddTestSettings();
                })
                .ConfigureServices(c =>
                {
                    c.ConfigureAll<ScriptApplicationHostOptions>(o =>
                    {
                        o.IsSelfHost = true;
                        o.LogPath = Path.Combine(uniqueTestRootPath, "logs");
                        o.SecretsPath = Path.Combine(uniqueTestRootPath, "secrets");
                        o.ScriptPath = _expectedScriptPath = scriptRootPath;
                    });

                    c.AddSingleton<IEnvironment>(_ => environment);
                    c.AddSingleton<IMetricsLogger>(_ => _metricsLogger);
                })
                .ConfigureScriptHostLogging(b =>
                {
                    b.AddProvider(_loggerProvider);
                });

            _httpServer = new TestServer(webHostBuilder);
            _httpClient = _httpServer.CreateClient();
            _httpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(_httpClient);

            var traces = _loggerProvider.GetAllLogMessages().ToArray();

            Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Host is in standby mode")));

            _expectedHostId = await _httpServer.Host.Services.GetService<IHostIdProvider>().GetHostIdAsync(CancellationToken.None);
        }


        protected async Task VerifyWarmupSucceeds(bool restart = false)
        {
            string uri = "api/warmup";
            if (restart)
            {
                uri += "?restart=1";
            }

            // issue warmup request and verify
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _httpClient.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 'OK'. Actual '{response.StatusCode}'. {_loggerProvider.GetLog()}");
            string responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal("WarmUp complete.", responseBody);
        }

        protected async Task<string[]> ListFunctions()
        {
            var secretManager = _httpServer.Host.Services.GetService<ISecretManagerProvider>().Current;
            var keys = await secretManager.GetHostSecretsAsync();
            string key = keys.MasterKey;

            string uri = $"admin/functions?code={key}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            var response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string responseBody = await response.Content.ReadAsStringAsync();
            var functions = JArray.Parse(responseBody);
            return functions.Select(p => (string)p.SelectToken("name")).ToArray();
        }

        private void CleanupTestDirectory()
        {
            try
            {
                FileUtility.DeleteDirectoryAsync(_testRootPath, true).GetAwaiter().GetResult();
            }
            catch
            {
                // best effort cleanup
            }
        }

        protected static object GetCachedTimeZoneInfo()
        {
            var cachedDataField = typeof(TimeZoneInfo).GetField("s_cachedData", BindingFlags.NonPublic | BindingFlags.Static);
            return cachedDataField.GetValue(null);
        }

        public virtual void Dispose()
        {
            _loggerProvider.Dispose();
            _httpServer.Dispose();
            _httpClient.Dispose();
            CleanupTestDirectory();
        }
    }
}
