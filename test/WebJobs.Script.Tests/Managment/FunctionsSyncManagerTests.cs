// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        private readonly Mock<IScriptWebHostEnvironment> _mockWebHostEnvironment;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly HostNameProvider _hostNameProvider;
        private string _function1;

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
                TestDataPath = @"x:\sampledata"
            };

            string testHostName = "appName.azurewebsites.net";
            _vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString() },
                { EnvironmentSettingNames.AzureWebsiteHostName, testHostName }
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
            var hostSecretsInfo = new HostSecretsInfo();
            hostSecretsInfo.MasterKey = "aaa";
            hostSecretsInfo.FunctionKeys = new Dictionary<string, string>
                {
                    { "TestHostFunctionKey1", "aaa" },
                    { "TestHostFunctionKey2", "bbb" }
                };
            hostSecretsInfo.SystemKeys = new Dictionary<string, string>
                {
                    { "TestSystemKey1", "aaa" },
                    { "TestSystemKey2", "bbb" }
                };
            secretManagerMock.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(hostSecretsInfo);
            Dictionary<string, string> functionSecretsResponse = new Dictionary<string, string>()
                {
                    { "TestFunctionKey1", "aaa" },
                    { "TestFunctionKey2", "bbb" }
                };
            secretManagerMock.Setup(p => p.GetFunctionSecretsAsync("function1", false)).ReturnsAsync(functionSecretsResponse);

            var configuration = ScriptSettingsManager.BuildDefaultConfiguration();
            var hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            hostIdProviderMock.Setup(p => p.GetHostIdAsync(CancellationToken.None)).ReturnsAsync("testhostid123");
            _mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>(MockBehavior.Strict);
            _mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(false);
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(testHostName);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType)).Returns("blob");
            _hostNameProvider = new HostNameProvider(_mockEnvironment.Object, loggerFactory.CreateLogger<HostNameProvider>());

            var functionMetadataProvider = new FunctionMetadataProvider(optionsMonitor, new OptionsWrapper<LanguageWorkerOptions>(CreateLanguageWorkerConfigSettings()), NullLogger<FunctionMetadataProvider>.Instance, new TestMetricsLogger());
            _functionsSyncManager = new FunctionsSyncManager(configuration, hostIdProviderMock.Object, optionsMonitor, loggerFactory.CreateLogger<FunctionsSyncManager>(), httpClient, secretManagerProviderMock.Object, _mockWebHostEnvironment.Object, _mockEnvironment.Object, _hostNameProvider, functionMetadataProvider);

            _expectedSyncTriggersPayload = "[{\"authLevel\":\"anonymous\",\"type\":\"httpTrigger\",\"direction\":\"in\",\"name\":\"req\",\"functionName\":\"function1\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"orchestrationTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function2\",\"taskHubName\":\"TestHubValue\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"activityTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function3\",\"taskHubName\":\"TestHubValue\"}]";
        }

        private void ResetMockFileSystem(string hostJsonContent = null)
        {
            var fileSystem = CreateFileSystem(_hostOptions, hostJsonContent);
            FileUtility.Instance = fileSystem;
        }

        [Fact]
        public async Task TrySyncTriggers_StandbyMode_ReturnsFalse()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                _mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(true);
                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_MaxSyncTriggersPayloadSize_Succeeds()
        {
            // create a dummy file that pushes us over size
            string maxString = new string('x', ScriptConstants.MaxTriggersStringLength + 1);
            _function1 = $"{{ bindings: [], test: '{maxString}'}}";

            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                Assert.True(_functionsSyncManager.ArmCacheEnabled);

                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.True(result.Success);

                string syncString = _contentBuilder.ToString();
                Assert.True(syncString.Length < ScriptConstants.MaxTriggersStringLength);
                var syncContent = JToken.Parse(syncString);
                Assert.Equal(JTokenType.Array, syncContent.Type);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_LocalEnvironment_ReturnsFalse()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment)).Returns("1");
                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public void ArmCacheEnabled_VerifyDefault()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns((string)null);
            Assert.True(_functionsSyncManager.ArmCacheEnabled);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TrySyncTriggers_PostsExpectedContent(bool cacheEnabled)
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns(cacheEnabled ? "1" : "0");

            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                Assert.Equal(_functionsSyncManager.ArmCacheEnabled, cacheEnabled);

                // Act
                var syncResult = await _functionsSyncManager.TrySyncTriggersAsync();

                // Assert
                Assert.True(syncResult.Success, "SyncTriggers should return success true");
                Assert.True(string.IsNullOrEmpty(syncResult.Error), "Error should be null or empty");

                // verify expected headers
                Assert.Equal(ScriptConstants.FunctionsUserAgent, _mockHttpHandler.LastRequest.Headers.UserAgent.ToString());
                Assert.True(_mockHttpHandler.LastRequest.Headers.Contains(ScriptConstants.AntaresLogIdHeaderName));

                if (cacheEnabled)
                {
                    // verify triggers
                    var result = JObject.Parse(_contentBuilder.ToString());
                    var triggers = result["triggers"];
                    Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));

                    // verify functions
                    var functions = (JArray)result["functions"];
                    Assert.Equal(3, functions.Count);

                    // verify secrets
                    var secrets = (JObject)result["secrets"];
                    var hostSecrets = (JObject)secrets["host"];
                    Assert.Equal("aaa", (string)hostSecrets["master"]);
                    var hostFunctionSecrets = (JObject)hostSecrets["function"];
                    Assert.Equal("aaa", (string)hostFunctionSecrets["TestHostFunctionKey1"]);
                    Assert.Equal("bbb", (string)hostFunctionSecrets["TestHostFunctionKey2"]);
                    var systemSecrets = (JObject)hostSecrets["system"];
                    Assert.Equal("aaa", (string)systemSecrets["TestSystemKey1"]);
                    Assert.Equal("bbb", (string)systemSecrets["TestSystemKey2"]);

                    var functionSecrets = (JArray)secrets["function"];
                    Assert.Equal(1, functionSecrets.Count);
                    var function1Secrets = (JObject)functionSecrets[0];
                    Assert.Equal("function1", function1Secrets["name"]);
                    Assert.Equal("aaa", (string)function1Secrets["secrets"]["TestFunctionKey1"]);
                    Assert.Equal("bbb", (string)function1Secrets["secrets"]["TestFunctionKey2"]);

                    var logs = _loggerProvider.GetAllLogMessages();
                    var log = logs[0];
                    int startIdx = log.FormattedMessage.IndexOf("Content=") + 8;
                    int endIdx = log.FormattedMessage.LastIndexOf(')');
                    var triggersLog = log.FormattedMessage.Substring(startIdx, endIdx - startIdx).Trim();
                    var logObject = JObject.Parse(triggersLog);

                    Assert.Equal(_expectedSyncTriggersPayload, logObject["triggers"].ToString(Formatting.None));
                    Assert.False(triggersLog.Contains("secrets"));
                }
                else
                {
                    var triggers = JArray.Parse(_contentBuilder.ToString());
                    Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));

                    var logs = _loggerProvider.GetAllLogMessages();
                    var log = logs[0];
                    int startIdx = log.FormattedMessage.IndexOf("Content=") + 8;
                    int endIdx = log.FormattedMessage.LastIndexOf(')');
                    var triggersLog = log.FormattedMessage.Substring(startIdx, endIdx - startIdx).Trim();
                    Assert.Equal(_expectedSyncTriggersPayload, triggersLog);
                }
            }
        }

        [Fact]
        public async Task TrySyncTriggers_CheckHash_PostsExpectedContent()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var hashBlob = await _functionsSyncManager.GetHashBlobAsync();
                await hashBlob.DeleteIfExistsAsync();

                var syncResult = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.True(syncResult.Success);
                Assert.Null(syncResult.Error);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                var result = JObject.Parse(_contentBuilder.ToString());
                var triggers = result["triggers"];
                Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));
                string hash = await hashBlob.DownloadTextAsync();
                Assert.Equal(64, hash.Length);

                // verify log statements
                var logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.True(logMessages[0].StartsWith("Making SyncTriggers request"));
                var startIdx = logMessages[0].IndexOf("Content=") + 8;
                var endIdx = logMessages[0].LastIndexOf(')');
                var sanitizedContent = logMessages[0].Substring(startIdx, endIdx - startIdx);
                var sanitizedObject = JObject.Parse(sanitizedContent);
                JToken value = null;
                var secretsLogged = sanitizedObject.TryGetValue("secrets", out value);
                Assert.False(secretsLogged);
                Assert.Equal("SyncTriggers call succeeded.", logMessages[1]);
                Assert.Equal($"SyncTriggers hash updated to '{hash}'", logMessages[2]);

                // now sync again - don't expect a sync triggers call this time
                _loggerProvider.ClearAllLogMessages();
                ResetMockFileSystem();
                _mockHttpHandler.Reset();
                syncResult = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.Equal(0, _mockHttpHandler.RequestCount);
                Assert.Equal(0, _contentBuilder.Length);
                Assert.True(syncResult.Success);
                Assert.Null(syncResult.Error);

                logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.Equal(1, logMessages.Length);
                Assert.Equal($"SyncTriggers hash (Last='{hash}', Current='{hash}')", logMessages[0]);

                // simulate a function change resulting in a new hash value
                ResetMockFileSystem("{}");
                _mockHttpHandler.Reset();
                syncResult = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                Assert.True(syncResult.Success);
                Assert.Null(syncResult.Error);
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
                var syncResult = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.False(syncResult.Success);
                string expectedErrorMessage = "SyncTriggers call failed (StatusCode=InternalServerError).";
                Assert.Equal(expectedErrorMessage, syncResult.Error);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                var result = JObject.Parse(_contentBuilder.ToString());
                var triggers = result["triggers"];
                Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));
                bool hashBlobExists = await hashBlob.ExistsAsync();
                Assert.False(hashBlobExists);

                // verify log statements
                var logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.True(logMessages[0].Contains("Content="));
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
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation)).Returns(skipSslValidation.ToString());
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns("sitename");

            var httpRequest = _functionsSyncManager.BuildSetTriggersRequest();
            Assert.Equal(syncTriggersUri, httpRequest.RequestUri.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, httpRequest.Method);
        }

        [Theory]
        [InlineData(null, "sitename", "https://sitename.azurewebsites.net/operations/settriggers")]
        [InlineData("hostname", null, "https://hostname/operations/settriggers")]
        public void Use_Website_Name_If_Website_Hostname_Is_Not_Available(string hostName, string siteName, string expectedSyncTriggersUri)
        {
            _hostNameProvider.Reset();
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns(siteName);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(hostName);

            var httpRequest = _functionsSyncManager.BuildSetTriggersRequest();
            Assert.Equal(expectedSyncTriggersUri, httpRequest.RequestUri.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, httpRequest.Method);
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

        private IFileSystem CreateFileSystem(ScriptApplicationHostOptions hostOptions, string hostJsonContent = null)
        {
            var rootPath = hostOptions.ScriptPath;
            string testDataPath = hostOptions.TestDataPath;

            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, "host.json"))).Returns(true);

            var durableConfig = new JObject
            {
                { "HubName", "TestHubValue" },
                { "azureStorageConnectionStringName", "DurableStorage" }
            };
            var extensionsConfig = new JObject
            {
                { "durableTask", durableConfig }
            };
            var defaultHostConfig = new JObject
            {
                { "extensions", extensionsConfig }
            };
            hostJsonContent = hostJsonContent ?? defaultHostConfig.ToString();
            var testHostJsonStream = new MemoryStream(Encoding.UTF8.GetBytes(hostJsonContent));
            testHostJsonStream.Position = 0;
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"host.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(testHostJsonStream);

            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);
            dirBase.Setup(d => d.Exists(rootPath)).Returns(true);
            dirBase.Setup(d => d.EnumerateDirectories(rootPath))
                .Returns(new[]
                {
                    Path.Combine(rootPath, "function1"),
                    Path.Combine(rootPath, "function2"),
                    Path.Combine(rootPath, "function3")
                });

            _function1 = @"{
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

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1\main.py"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function1\function.json"))).Returns(_function1);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function1\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(_function1));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function1.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(_function1));
            });

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function2\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function2\main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function2\function.json"))).Returns(function2);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function2\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function2));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function2.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(_function1));
            });

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function3\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function3\main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function3\function.json"))).Returns(function3);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function3\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function3));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function3.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(_function1));
            });

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

            public HttpRequestMessage LastRequest { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                RequestCount++;
                _content.Append(await request.Content.ReadAsStringAsync());
                return new HttpResponseMessage(MockStatusCode);
            }

            public void Reset()
            {
                LastRequest = null;
                _content.Clear();
                RequestCount = 0;
            }
        }
    }
}