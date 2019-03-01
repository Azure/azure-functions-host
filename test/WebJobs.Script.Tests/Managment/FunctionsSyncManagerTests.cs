// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class FunctionsSyncManagerTests : IDisposable
    {
        private readonly string _testRootScriptPath;
        private readonly string _testHostConfigFilePath;
        private readonly ScriptApplicationHostOptions _hostOptions;
        private readonly FunctionsSyncManager _functionsSyncManager;
        private readonly Dictionary<string, string> _vars;
        private readonly StringBuilder _contentBuilder;
        private readonly string _expectedSyncTriggersPayload;
        private readonly MockHttpHandler _mockHttpHandler;
        private readonly TestLoggerProvider _loggerProvider;

        public FunctionsSyncManagerTests()
        {
            _testRootScriptPath = Path.GetTempPath();
            _testHostConfigFilePath = Path.Combine(_testRootScriptPath, ScriptConstants.HostMetadataFileName);
            FileUtility.DeleteFileSafe(_testHostConfigFilePath);

            _hostOptions = new ScriptApplicationHostOptions
            {
                ScriptPath = @"x:\root",
                IsSelfHost = false,
                LogPath = @"x:\tmp\log",
                SecretsPath = @"x:\secrets",
                TestDataPath = @"x:\test"
            };

            _vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString() },
                { EnvironmentSettingNames.AzureWebsiteHostName, "appName.azurewebsites.net" }
            };

            ResetMockFileSystem();

            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _contentBuilder = new StringBuilder();
            _mockHttpHandler = new MockHttpHandler(_contentBuilder);
            var httpClient = CreateHttpClient(_mockHttpHandler);
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_hostOptions);
            var tokenSource = new TestChangeTokenSource();
            var changeTokens = new[] { tokenSource };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);
            var secretManagerProviderMock = new Mock<ISecretManagerProvider>(MockBehavior.Strict);
            var secretManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);
            secretManagerProviderMock.SetupGet(p => p.Current).Returns(secretManagerMock.Object);

            var configuration = ScriptSettingsManager.BuildDefaultConfiguration();
            var hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            hostIdProviderMock.Setup(p => p.GetHostIdAsync(CancellationToken.None)).ReturnsAsync("testhostid123");
            _functionsSyncManager = new FunctionsSyncManager(configuration, hostIdProviderMock.Object, optionsMonitor, new OptionsWrapper<LanguageWorkerOptions>(CreateLanguageWorkerConfigSettings()), loggerFactory, httpClient, secretManagerProviderMock.Object);

            _expectedSyncTriggersPayload = "[{\"authLevel\":\"anonymous\",\"type\":\"httpTrigger\",\"direction\":\"in\",\"name\":\"req\",\"functionName\":\"function1\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"orchestrationTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function2\",\"taskHubName\":\"TestHubValue\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"activityTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function3\",\"taskHubName\":\"TestHubValue\"}]";
        }

        private void ResetMockFileSystem(string hostJsonContent = null)
        {
            var fileSystem = CreateFileSystem(_hostOptions.ScriptPath, hostJsonContent);
            FileUtility.Instance = fileSystem;
        }

        [Fact]
        public async Task TrySyncTriggers_PostsExpectedContent()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                // Act
                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                var triggers = JArray.Parse(_contentBuilder.ToString());

                // Assert
                Assert.True(result.Success, "SyncTriggers should return success true");
                Assert.True(string.IsNullOrEmpty(result.Error), "Error should be null or empty");

                // verify triggers
                Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));
            }
        }

        [Fact]
        public async Task TrySyncTriggers_CheckHash_PostsExpectedContent()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var hashBlob = await _functionsSyncManager.GetHashBlobAsync();
                await hashBlob.DeleteIfExistsAsync();

                var result = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.True(result.Success);
                Assert.Null(result.Error);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                var triggers = JArray.Parse(_contentBuilder.ToString());
                Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));
                string hash = await hashBlob.DownloadTextAsync();
                Assert.Equal(64, hash.Length);

                // verify log statements
                var logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.True(logMessages[0].StartsWith("SyncTriggers content: [{"));
                Assert.Equal("SyncTriggers call succeeded.", logMessages[1]);
                Assert.Equal($"SyncTriggers hash updated to '{hash}'", logMessages[2]);

                // now sync again - don't expect a sync triggers call this time
                _loggerProvider.ClearAllLogMessages();
                ResetMockFileSystem();
                _mockHttpHandler.Reset();
                result = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.Equal(0, _mockHttpHandler.RequestCount);
                Assert.Equal(0, _contentBuilder.Length);
                Assert.True(result.Success);
                Assert.Null(result.Error);

                logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.Equal(1, logMessages.Length);
                Assert.Equal($"SyncTriggers hash (Last='{hash}', Current='{hash}')", logMessages[0]);

                // simulate a function change resulting in a new hash value
                ResetMockFileSystem("{}");
                _mockHttpHandler.Reset();
                result = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                Assert.True(result.Success);
                Assert.Null(result.Error);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_CheckHash_SetTriggersFailure_HashNotUpdated()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var hashBlob = await _functionsSyncManager.GetHashBlobAsync();
                await hashBlob.DeleteIfExistsAsync();

                _mockHttpHandler.MockStatusCode = HttpStatusCode.InternalServerError;
                var result = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.False(result.Success);
                string expectedErrorMessage = "SyncTriggers call failed. StatusCode=InternalServerError";
                Assert.Equal(expectedErrorMessage, result.Error);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                var triggers = JArray.Parse(_contentBuilder.ToString());
                Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));
                bool hashBlobExists = await hashBlob.ExistsAsync();
                Assert.False(hashBlobExists);

                // verify log statements
                var logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.True(logMessages[0].StartsWith("SyncTriggers content: [{"));
                Assert.Equal(expectedErrorMessage, logMessages[1]);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("notaconnectionstring")]
        public async Task CheckAndUpdateHashAsync_ReturnsExpectedValue(string storageConnectionString)
        {
            _vars.Add("AzureWebJobsStorage", storageConnectionString);
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var blob = await _functionsSyncManager.GetHashBlobAsync();
                Assert.Null(blob);
            }
        }

        [Theory]
        [InlineData(1, "http://sitename/operations/settriggers")]
        [InlineData(0, "https://sitename/operations/settriggers")]
        public void Disables_Ssl_If_SkipSslValidation_Enabled(int skipSslValidation, string syncTriggersUri)
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.SkipSslValidation, skipSslValidation.ToString() },
                { EnvironmentSettingNames.AzureWebsiteHostName, "sitename" },
            };

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var httpRequest = FunctionsSyncManager.BuildSetTriggersRequest();
                Assert.Equal(syncTriggersUri, httpRequest.RequestUri.AbsoluteUri);
                Assert.Equal(HttpMethod.Post, httpRequest.Method);
            }
        }

        [Theory]
        [InlineData(EnvironmentSettingNames.AzureWebsiteName, "sitename", "https://sitename.azurewebsites.net/operations/settriggers")]
        [InlineData(EnvironmentSettingNames.AzureWebsiteHostName, "sitename", "https://sitename/operations/settriggers")]
        public void Use_Website_Name_If_Website_Hostname_Is_Not_Available(string envKey, string envValue, string expectedSyncTriggersUri)
        {
            var vars = new Dictionary<string, string>
            {
                { envKey, envValue },
            };

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                var httpRequest = FunctionsSyncManager.BuildSetTriggersRequest();
                Assert.Equal(expectedSyncTriggersUri, httpRequest.RequestUri.AbsoluteUri);
                Assert.Equal(HttpMethod.Post, httpRequest.Method);
            }
        }

        private static HttpClient CreateHttpClient(MockHttpHandler httpHandler)
        {
            return new HttpClient(httpHandler);
        }

        private static LanguageWorkerOptions CreateLanguageWorkerConfigSettings()
        {
            return new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
        }

        private static IFileSystem CreateFileSystem(string rootPath, string hostJsonContent = null)
        {
            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, "host.json"))).Returns(true);

            hostJsonContent = hostJsonContent ?? @"{ ""durableTask"": { ""HubName"": ""TestHubValue"", ""azureStorageConnectionStringName"": ""DurableStorage"" }}";
            var testHostJsonStream = new MemoryStream(Encoding.UTF8.GetBytes(hostJsonContent));
            testHostJsonStream.Position = 0;
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"host.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(testHostJsonStream);

            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);

            dirBase.Setup(d => d.EnumerateDirectories(rootPath))
                .Returns(new[]
                {
                    @"x:\root\function1",
                    @"x:\root\function2",
                    @"x:\root\function3"
                });

            var function1 = @"{
  ""scriptFile"": ""main.py"",
  ""disabled"": false,
  ""bindings"": [
    {
      ""authLevel"": ""anonymous"",
      ""type"": ""httpTrigger"",
      ""direction"": ""in"",
      ""name"": ""req""
    },
    {
      ""type"": ""http"",
      ""direction"": ""out"",
      ""name"": ""$return""
    }
  ]
}";
            var function2 = @"{
  ""disabled"": false,
  ""scriptFile"": ""main.js"",
  ""bindings"": [
    {
      ""name"": ""myQueueItem"",
      ""type"": ""orchestrationTrigger"",
      ""direction"": ""in"",
      ""queueName"": ""myqueue-items"",
      ""connection"": """"
    }
  ]
}";

            var function3 = @"{
  ""disabled"": false,
  ""scriptFile"": ""main.js"",
  ""bindings"": [
    {
      ""name"": ""myQueueItem"",
      ""type"": ""activityTrigger"",
      ""direction"": ""in"",
      ""queueName"": ""myqueue-items"",
      ""connection"": """"
    }
  ]
}";
            var function1Stream = new MemoryStream(Encoding.UTF8.GetBytes(function1));
            function1Stream.Position = 0;
            var function2Stream = new MemoryStream(Encoding.UTF8.GetBytes(function2));
            function2Stream.Position = 0;
            var function3Stream = new MemoryStream(Encoding.UTF8.GetBytes(function3));
            function3Stream.Position = 0;
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1\main.py"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function1\function.json"))).Returns(function1);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function1\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(function1Stream);

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function2\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function2\main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function2\function.json"))).Returns(function2);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function2\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(function2Stream);

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function3\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function3\main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function3\function.json"))).Returns(function3);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function3\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(function3Stream);

            return fileSystem.Object;
        }

        public void Dispose()
        {
            // Clean up mock IFileSystem
            FileUtility.Instance = null;
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, string.Empty);
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", string.Empty);
            FileUtility.DeleteFileSafe(_testHostConfigFilePath);
        }

        private class MockHttpHandler : HttpClientHandler
        {
            private StringBuilder _content;

            public MockHttpHandler(StringBuilder content)
            {
                _content = content;
                MockStatusCode = HttpStatusCode.OK;
            }

            public int RequestCount { get; set; }

            public HttpStatusCode MockStatusCode { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                _content.Append(await request.Content.ReadAsStringAsync());
                return new HttpResponseMessage(MockStatusCode);
            }

            public void Reset()
            {
                _content.Clear();
                RequestCount = 0;
            }
        }
    }
}
