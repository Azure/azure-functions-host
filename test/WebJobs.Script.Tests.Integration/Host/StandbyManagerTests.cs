// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.StandbyModeTests)]
    public class StandbyManagerTests : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestLoggerProvider _loggerProvider;
        private HttpClient _httpClient;
        private TestServer _httpServer;
        private string _expectedHostId;
        private ScriptWebHostOptions _webHostOptions;
        private string _testRootPath;

        public StandbyManagerTests()
        {
            _settingsManager = ScriptSettingsManager.Instance;
            _testRootPath = Path.Combine(Path.GetTempPath(), "StandbyManagerTests");
            ResetEnvironment();
            CleanupTestDirectory();
        }

        [Fact]
        public void IsWarmUpRequest_ReturnsExpectedValue()
        {
            var environment = new ScriptWebHostEnvironment();
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            Assert.False(StandbyManager.IsWarmUpRequest(request, environment));

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, null }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {   
                // Get a new instance of the host environemnt, which
                // will reset the standby flag.
                environment = new ScriptWebHostEnvironment();

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(StandbyManager.IsWarmUpRequest(request, environment));

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
                Assert.True(StandbyManager.IsWarmUpRequest(request, environment));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/csharphttpwarmup");
                Assert.True(StandbyManager.IsWarmUpRequest(request, environment));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
                request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
                Assert.False(StandbyManager.IsWarmUpRequest(request, environment));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/foo");
                Assert.False(StandbyManager.IsWarmUpRequest(request, environment));
            }

            vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0" },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, null }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // Get a new instance of the host environemnt, which
                // will reset the standby flag.
                environment = new ScriptWebHostEnvironment();

                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
                Assert.False(StandbyManager.IsWarmUpRequest(request, environment));

                request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
                _settingsManager.SetSetting(EnvironmentSettingNames.ContainerName, "TestContainer");
                Assert.True(SystemEnvironment.Instance.IsLinuxContainerEnvironment());
                Assert.True(StandbyManager.IsWarmUpRequest(request, environment));
            }
        }

        [Fact]
        public async Task StandbyMode_EndToEnd()
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { EnvironmentSettingNames.AzureWebsiteSku, "Dynamic" },
                { EnvironmentSettingNames.AzureWebsiteHomePath, null },
                { EnvironmentSettingNames.AzureWebsiteInstanceId, "87654639876900123453445678890144" },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                InitializeTestHost("Windows");

                await VerifyWarmupSucceeds();
                await VerifyWarmupSucceeds(restart: true);

                // now specialize the host
                ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");

                Assert.False(new ScriptWebHostEnvironment().InStandbyMode);
                Assert.True(ScriptSettingsManager.Instance.ContainerReady);

                _httpServer.Dispose();
                _httpClient.Dispose();

                // give time for the specialization to happen
                string[] logLines = null;
                await TestHelpers.Await(() =>
                {
                    // wait for the trace indicating that the host has been specialized
                    logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                    return logLines.Contains("Generating 0 job function(s)");
                }, userMessageCallback: () => string.Join(Environment.NewLine, _loggerProvider.GetAllLogMessages().Select(p => $"[{p.Timestamp.ToString("HH:mm:ss.fff")}] {p.FormattedMessage}")));

                // verify the rest of the expected logs
                Assert.True(logLines.Count(p => p.Contains("Stopping Host")) >= 1);
                Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
                Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Starting Host (HostId=placeholder-host")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
                Assert.Equal(1, logLines.Count(p => p.Contains($"Starting Host (HostId={_expectedHostId}")));
                Assert.Contains("Generating 0 job function(s)", logLines);
            }
        }

        [Fact]
        public async Task StandbyMode_EndToEnd_LinuxContainer()
        {
            byte[] bytes = TestHelpers.GenerateKeyBytes();
            var encryptionKey = Convert.ToBase64String(bytes);

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.ContainerName, "TestApp" },
                { EnvironmentSettingNames.AzureWebsiteName, "TestApp" },
                { EnvironmentSettingNames.ContainerEncryptionKey, encryptionKey },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { EnvironmentSettingNames.AzureWebsiteSku, "Dynamic" },
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, null },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                InitializeTestHost("Linux");

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
                var secretManager = _httpServer.Host.Services.GetService<ISecretManager>();
                var keys = await secretManager.GetFunctionSecretsAsync("HttpTrigger");
                string key = keys.First().Value;
                request = new HttpRequestMessage(HttpMethod.Get, $"api/httptrigger?code={key}");
                request.Headers.Add(ScriptConstants.AntaresColdStartHeaderName, "1");
                response = await _httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                Assert.False(new ScriptWebHostEnvironment().InStandbyMode);
                Assert.True(ScriptSettingsManager.Instance.ContainerReady);

                // verify warmup function no longer there
                request = new HttpRequestMessage(HttpMethod.Get, "api/warmup");
                response = await _httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

                _httpServer.Dispose();
                _httpClient.Dispose();

                // verify the expected logs
                var logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                Assert.True(logLines.Count(p => p.Contains("Stopping Host")) >= 1);
                Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
                Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Starting Host (HostId=placeholder-host")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
                Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Validating host assignment context")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Starting Assignment")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Applying 1 app setting(s)")));
                Assert.Equal(1, logLines.Count(p => p.Contains($"Extracting files to '{_webHostOptions.ScriptPath}'")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Zip extraction complete")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Triggering specialization")));
                Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
                Assert.Equal(1, logLines.Count(p => p.Contains($"Starting Host (HostId={_expectedHostId}")));
                Assert.Contains("Node.js HttpTrigger function invoked.", logLines);

                // verify cold start log entry
                var coldStartLog = _loggerProvider.GetAllLogMessages().FirstOrDefault(p => p.Category == ScriptConstants.LogCategoryHostMetrics);
                JObject coldStartData = JObject.Parse(coldStartLog.FormattedMessage);
                Assert.Equal("Dynamic", coldStartData["sku"]);
                Assert.True((int)coldStartData["dispatchDuration"] > 0);
                Assert.True((int)coldStartData["functionDuration"] > 0);
            }
        }

        private void InitializeTestHost(string testDirName)
        {
            var httpConfig = new HttpConfiguration();
            var uniqueTestRootPath = Path.Combine(_testRootPath, testDirName, Guid.NewGuid().ToString());

            _loggerProvider = new TestLoggerProvider();
            var loggerProviderFactory = new TestLoggerProviderFactory(_loggerProvider);
            _webHostOptions = new ScriptWebHostOptions
            {
                IsSelfHost = true,
                LogPath = Path.Combine(uniqueTestRootPath, "Logs"),
                SecretsPath = Path.Combine(uniqueTestRootPath, "Secrets"),
                ScriptPath = Path.Combine(uniqueTestRootPath, "WWWRoot")
            };

            if (_settingsManager.IsAppServiceEnvironment)
            {
                // if the test is mocking App Service environment, we need
                // to also set the HOME variable
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, uniqueTestRootPath);
            }

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var webHostBuilder = Program.CreateWebHostBuilder()
                .ConfigureServices(c =>
                {
                    c.AddSingleton(_webHostOptions)
                    .AddSingleton<ILoggerProviderFactory>(loggerProviderFactory)
                    .AddSingleton<ILoggerFactory>(loggerFactory);
                });

            _httpServer = new TestServer(webHostBuilder);
            _httpClient = _httpServer.CreateClient();
            _httpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(_httpClient);

            var traces = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Starting Host (HostId=placeholder-host")));
            Assert.NotNull(traces.Single(p => p.FormattedMessage.StartsWith("Host is in standby mode")));

            var hostConfig = _webHostOptions.ToScriptHostConfiguration(true);
            // TODO: DI (FACAVAL) Review
            // _expectedHostId = hostConfig.HostConfig.HostId;
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
            var secretManager = _httpServer.Host.Services.GetService<ISecretManager>();
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

        private async Task VerifyWarmupSucceeds(bool restart = false)
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

        private static async Task<Uri> CreateBlobSas(string filePath, string blobContainer, string blobName)
        {
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);
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

        public void Dispose()
        {
            ResetEnvironment();
            CleanupTestDirectory();
        }

        private void CleanupTestDirectory()
        {
            var testRootPath = Path.Combine(Path.GetTempPath(), "StandbyManagerTests");
            try
            {
                FileUtility.DeleteDirectoryAsync(testRootPath, true);
            }
            catch
            {
                // best effort cleanup
            }
        }

        private void ResetEnvironment()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteZipDeployment, string.Empty);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath, null);
        }
    }
}
