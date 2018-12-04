// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.StandbyModeTestsWindows)]
    public class StandbyManagerE2ETests_Windows : StandbyManagerE2ETestBase
    {
        [Fact]
        public async Task StandbyModeE2E()
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { EnvironmentSettingNames.AzureWebsiteSku, "Dynamic" },
                { EnvironmentSettingNames.AzureWebsiteHomePath, null },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "87654639876900123453445678890144" }
            };

            var environment = new TestEnvironment(vars);
            await InitializeTestHostAsync("Windows", environment);

            await VerifyWarmupSucceeds();
            await VerifyWarmupSucceeds(restart: true);

            // now specialize the host
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");

            Assert.False(environment.IsPlaceholderModeEnabled());
            Assert.True(environment.IsContainerReady());

            // give time for the specialization to happen
            string[] logLines = null;
            await TestHelpers.Await(() =>
            {
                // wait for the trace indicating that the host has been specialized
                logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                return logLines.Contains("Generating 0 job function(s)") && logLines.Contains("Stopping JobHost");
            }, userMessageCallback: () => string.Join(Environment.NewLine, _loggerProvider.GetAllLogMessages().Select(p => $"[{p.Timestamp.ToString("HH:mm:ss.fff")}] {p.FormattedMessage}")));

            _httpServer.Dispose();
            _httpClient.Dispose();

            // verify the rest of the expected logs
            logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
            Assert.True(logLines.Count(p => p.Contains("Stopping JobHost")) >= 1);
            Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
            Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
            Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
            Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
            Assert.Equal(3, logLines.Count(p => p.Contains($"Starting Host (HostId={_expectedHostId}")));
            Assert.Equal(3, logLines.Count(p => p.Contains($"Loading functions metadata")));
            Assert.Equal(2, logLines.Count(p => p.Contains($"1 functions loaded")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"0 functions loaded")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"Loading proxies metadata")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Initializing Azure Function proxies")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"0 proxies loaded")));
            Assert.Contains("Generating 0 job function(s)", logLines);

            // Verify that the internal cache has reset
            Assert.NotSame(GetCachedTimeZoneInfo(), _originalTimeZoneInfoCache);
        }
    }

    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.StandbyModeTestsLinux)]
    public class StandbyManagerE2ETests_Linux : StandbyManagerE2ETestBase
    {
        [Fact]
        public async Task StandbyModeE2E_LinuxContainer()
        {
            byte[] bytes = TestHelpers.GenerateKeyBytes();
            var encryptionKey = Convert.ToBase64String(bytes);

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.ContainerName, "TestApp" },
                { EnvironmentSettingNames.AzureWebsiteName, "TestApp" },
                { EnvironmentSettingNames.ContainerEncryptionKey, encryptionKey },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { EnvironmentSettingNames.AzureWebsiteSku, "Dynamic" },
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, null },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" }
            };

            var environment = new TestEnvironment(vars);

            await InitializeTestHostAsync("Linux", environment);

            // verify only the Warmup function is present
            // generally when in placeholder mode, the list API won't be called
            // but we're doing this for regression testing
            var functions = await ListFunctions();
            Assert.Equal(1, functions.Length);
            Assert.Equal("WarmUp", functions[0]);

            await VerifyWarmupSucceeds();
            await VerifyWarmupSucceeds(restart: true);

            // now specialize the site
            await Assign(encryptionKey);

            // immediately call a function - expect the call to block until
            // the host is fully specialized
            // the Unauthorized is expected since we havne't specified the key
            // it's enough here to ensure we don't get a 404
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/httptrigger");
            request.Headers.Add(ScriptConstants.AntaresColdStartHeaderName, "1");
            var response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // now that the host is initialized, send a valid key
            // and expect success
            var secretManager = _httpServer.Host.Services.GetService<ISecretManagerProvider>().Current;
            var keys = await secretManager.GetFunctionSecretsAsync("HttpTrigger");
            string key = keys.First().Value;
            request = new HttpRequestMessage(HttpMethod.Get, $"api/httptrigger?code={key}");
            request.Headers.Add(ScriptConstants.AntaresColdStartHeaderName, "1");
            response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.False(environment.IsPlaceholderModeEnabled());
            Assert.True(environment.IsContainerReady());

            // verify that after specialization the correct
            // app content is returned
            functions = await ListFunctions();
            Assert.Equal(1, functions.Length);
            Assert.Equal("HttpTrigger", functions[0]);

            // verify warmup function no longer there
            request = new HttpRequestMessage(HttpMethod.Get, "api/warmup");
            response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            _httpServer.Dispose();
            _httpClient.Dispose();

            string sanitizedMachineName = Environment.MachineName
                    .Where(char.IsLetterOrDigit)
                    .Aggregate(new StringBuilder(), (b, c) => b.Append(c))
                    .ToString().ToLowerInvariant();

            // verify the expected logs
            var logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
            Assert.True(logLines.Count(p => p.Contains("Stopping JobHost")) >= 1);
            Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
            Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
            Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
            Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Validating host assignment context")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Starting Assignment")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Applying 1 app setting(s)")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"Extracting files to '{_expectedScriptPath}'")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Zip extraction complete")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Triggering specialization")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
            Assert.Equal(3, logLines.Count(p => p.Contains($"Starting Host (HostId={sanitizedMachineName}")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"Loading proxies metadata")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Initializing Azure Function proxies")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"0 proxies loaded")));
            Assert.Contains("Node.js HttpTrigger function invoked.", logLines);

            // verify cold start log entry
            var coldStartLog = _loggerProvider.GetAllLogMessages().FirstOrDefault(p => p.Category == ScriptConstants.LogCategoryHostMetrics);
            JObject coldStartData = JObject.Parse(coldStartLog.FormattedMessage);
            Assert.Equal("Dynamic", coldStartData["sku"]);
            Assert.True((int)coldStartData["dispatchDuration"] > 0);
            Assert.True((int)coldStartData["functionDuration"] > 0);

            // Verify that the internal cache has reset
            Assert.NotSame(GetCachedTimeZoneInfo(), _originalTimeZoneInfoCache);
        }

        private async Task Assign(string encryptionKey)
        {
            // create a zip package
            var contentRoot = Path.Combine(Path.GetTempPath(), @"FunctionsTest");
            var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), @"TestScripts\Node\HttpTrigger");
            var zipFilePath = Path.Combine(contentRoot, "content.zip");
            await CreateContentZip(contentRoot, zipFilePath, @"TestScripts\Node\HttpTrigger");

            // upload the blob and get a SAS uri
            var sasUri = await CreateBlobSas(zipFilePath, "azure-functions-test", "appcontents.zip");

            // Now specialize the host by invoking assign
            var secretManager = _httpServer.Host.Services.GetService<ISecretManagerProvider>().Current;
            var masterKey = (await secretManager.GetHostSecretsAsync()).MasterKey;
            string uri = "admin/instance/assign";
            var request = new HttpRequestMessage(HttpMethod.Post, uri);
            var environment = new Dictionary<string, string>()
                {
                    { EnvironmentSettingNames.AzureWebsiteZipDeployment, sasUri.ToString() }
                };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestApp",
                Environment = environment
            };
            var encryptedAssignmentContext = EncryptedHostAssignmentContext.Create(assignmentContext, encryptionKey);
            string json = JsonConvert.SerializeObject(encryptedAssignmentContext);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);
            var response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        private static async Task CreateContentZip(string contentRoot, string zipPath, params string[] copyDirs)
        {
            var contentTemp = Path.Combine(contentRoot, @"ZipContent");
            await FileUtility.DeleteDirectoryAsync(contentTemp, true);

            foreach (var sourceDir in copyDirs)
            {
                var directoryName = Path.GetFileName(sourceDir);
                var targetPath = Path.Combine(contentTemp, directoryName);
                FileUtility.EnsureDirectoryExists(targetPath);
                var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), sourceDir);
                FileUtility.CopyDirectory(sourcePath, targetPath);
            }

            FileUtility.DeleteFileSafe(zipPath);
            ZipFile.CreateFromDirectory(contentTemp, zipPath);
        }

        private async Task<Uri> CreateBlobSas(string filePath, string blobContainer, string blobName)
        {
            var configuration = _httpServer.Host.Services.GetService<IConfiguration>();
            string connectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(blobContainer);
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlockBlobReference(blobName);
            await blob.UploadFromFileAsync(filePath);
            var policy = new SharedAccessBlobPolicy
            {
                SharedAccessStartTime = DateTime.UtcNow,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List
            };
            var sas = blob.GetSharedAccessSignature(policy);
            var sasUri = new Uri(blob.Uri, sas);

            return sasUri;
        }
    }

    public class StandbyManagerE2ETestBase : IDisposable
    {
        protected readonly string _testRootPath;
        protected string _expectedHostId;
        protected TestLoggerProvider _loggerProvider;
        protected string _expectedScriptPath;
        protected HttpClient _httpClient;
        protected TestServer _httpServer;
        protected readonly object _originalTimeZoneInfoCache = GetCachedTimeZoneInfo();

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
                .ConfigureLogging(c =>
                {
                    c.AddProvider(_loggerProvider);
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
                    c.AddSingleton<IConfigureBuilder<ILoggingBuilder>>(new DelegatedConfigureBuilder<ILoggingBuilder>(b => b.AddProvider(_loggerProvider)));
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
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
            CleanupTestDirectory();
        }
    }
}
