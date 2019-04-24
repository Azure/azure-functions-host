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
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
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
        private readonly ScriptHostConfiguration _hostConfig;
        private readonly FunctionsSyncManager _functionsSyncManager;
        private readonly Dictionary<string, string> _vars;
        private readonly StringBuilder _contentBuilder;
        private readonly string _expectedSyncTriggersPayload;
        private readonly MockHttpHandler _mockHttpHandler;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly Mock<ScriptSettingsManager> _scriptSettingsManagerMock;

        public FunctionsSyncManagerTests()
        {
            _testRootScriptPath = Path.GetTempPath();
            _testHostConfigFilePath = Path.Combine(_testRootScriptPath, ScriptConstants.HostMetadataFileName);
            FileUtility.DeleteFileSafe(_testHostConfigFilePath);

            _hostConfig = new ScriptHostConfiguration
            {
                RootScriptPath = @"x:\root\site\wwwroot",
                IsSelfHost = false,
                RootLogPath = @"x:\root\LogFiles\Application\Functions",
                TestDataPath = @"x:\root\data\functions\sampledata"
            };
            _hostConfig.HostConfig.HostId = "testhostid123";

            _vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.WebsiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString() },
                { EnvironmentSettingNames.AzureWebsiteHostName, "appName.azurewebsites.net" },
                { EnvironmentSettingNames.AzureWebsiteName, "appName" },
                { EnvironmentSettingNames.AzureWebsiteHomePath, @"x:\root" }
            };

            ResetMockFileSystem();

            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _contentBuilder = new StringBuilder();
            _mockHttpHandler = new MockHttpHandler(_contentBuilder);
            var httpClient = CreateHttpClient(_mockHttpHandler);
            var secretManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);
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

            _scriptSettingsManagerMock = new Mock<ScriptSettingsManager>(MockBehavior.Strict);
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns("1");
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.WebsiteAuthEncryptionKey)).Returns("abc123");
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath)).Returns((string)null);
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode)).Returns((string)null);
            _scriptSettingsManagerMock.SetupGet(p => p.IsCoreToolsEnvironment).Returns(false);
            _scriptSettingsManagerMock.SetupGet(p => p.ContainerReady).Returns(true);
            _scriptSettingsManagerMock.SetupGet(p => p.ConfigurationReady).Returns(true);

            _functionsSyncManager = new FunctionsSyncManager(_hostConfig, loggerFactory, secretManagerMock.Object, _scriptSettingsManagerMock.Object, httpClient);

            _expectedSyncTriggersPayload = "[{\"authLevel\":\"anonymous\",\"type\":\"httpTrigger\",\"direction\":\"in\",\"name\":\"req\",\"functionName\":\"function1\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"orchestrationTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function2\",\"taskHubName\":\"TestHubValue\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"activityTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function3\",\"taskHubName\":\"TestHubValue\"}]";

            HostNameProvider.Reset();
        }

        private void ResetMockFileSystem(string hostJsonContent = null)
        {
            var fileSystem = CreateFileSystem(_hostConfig, hostJsonContent);
            FileUtility.Instance = fileSystem;
        }

        [Fact]
        public async Task TrySyncTriggers_NoEncryptionKey_ReturnsFalse()
        {
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.WebsiteAuthEncryptionKey)).Returns((string)null);
            _vars.Remove(EnvironmentSettingNames.WebsiteAuthEncryptionKey);

            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_StandbyMode_ReturnsFalse()
        {
            _vars.Add(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_LocalEnvironment_ReturnsFalse()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                _scriptSettingsManagerMock.SetupGet(p => p.IsCoreToolsEnvironment).Returns(true);

                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_ContainerNotReady_ReturnsFalse()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                _scriptSettingsManagerMock.SetupGet(p => p.ContainerReady).Returns(false);

                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public async Task TrySyncTriggers_ConfigurationNotReady_ReturnsFalse()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                _scriptSettingsManagerMock.SetupGet(p => p.ConfigurationReady).Returns(false);

                var result = await _functionsSyncManager.TrySyncTriggersAsync();
                Assert.False(result.Success);
                var expectedMessage = "Invalid environment for SyncTriggers operation.";
                Assert.Equal(expectedMessage, result.Error);
            }
        }

        [Fact]
        public void ArmCacheEnabled_VerifyDefault()
        {
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns((string)null);
            Assert.False(_functionsSyncManager.ArmCacheEnabled);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TrySyncTriggers_PostsExpectedContent(bool cacheEnabled)
        {
            _scriptSettingsManagerMock.Setup(p => p.GetSetting(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled)).Returns(cacheEnabled ? "1" : "0");

            Assert.Equal(cacheEnabled, _functionsSyncManager.ArmCacheEnabled);

            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                // Act
                var syncResult = await _functionsSyncManager.TrySyncTriggersAsync();

                // Assert
                Assert.True(syncResult.Success, "SyncTriggers should return success true");
                Assert.True(string.IsNullOrEmpty(syncResult.Error), "Error should be null or empty");

                if (cacheEnabled)
                {
                    // verify triggers
                    var result = JObject.Parse(_contentBuilder.ToString());
                    var triggers = result["triggers"];
                    Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));

                    Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));

                    // verify functions
                    var functions = (JArray)result["functions"];
                    Assert.Equal(3, functions.Count);

                    // verify all file access hrefs point to the SCM site
                    var function = functions[0].ToObject<FunctionMetadataResponse>();
                    Assert.Equal("function1", function.Name);
                    Assert.Equal(ScriptType.Python.ToString(), function.Language);
                    Assert.Equal("https://appname.scm.azurewebsites.net/api/vfs/site/wwwroot/function1/main.py", function.ScriptHref.ToString());
                    Assert.Equal("https://appname.scm.azurewebsites.net/api/vfs/site/wwwroot/function1/function.json", function.ConfigHref.ToString());
                    Assert.Equal("https://appname.scm.azurewebsites.net/api/vfs/site/wwwroot/function1/", function.ScriptRootPathHref.ToString());
                    Assert.Equal("https://appname.scm.azurewebsites.net/api/functions/function1", function.Href.ToString());
                    Assert.Equal("https://appname.scm.azurewebsites.net/api/vfs/data/functions/sampledata/function1.dat", function.TestDataHref.ToString());
                    Assert.Equal("https://appname.azurewebsites.net/api/function1", function.InvokeUrlTemplate.ToString());

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
                    var log = logs.First();
                    int idx = log.FormattedMessage.IndexOf(':');
                    var triggersLog = log.FormattedMessage.Substring(idx + 1).Trim();
                    var logObject = JObject.Parse(triggersLog);

                    Assert.Equal(_expectedSyncTriggersPayload, logObject["triggers"].ToString(Formatting.None));
                    Assert.False(triggersLog.Contains("secrets"));
                }
                else
                {
                    var triggers = JArray.Parse(_contentBuilder.ToString());
                    Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));

                    var logs = _loggerProvider.GetAllLogMessages();
                    var log = logs.First();
                    int idx = log.FormattedMessage.IndexOf(':');
                    var triggersLog = log.FormattedMessage.Substring(idx + 1).Trim();
                    Assert.Equal(_expectedSyncTriggersPayload, triggersLog);
                }
            }
        }

        [Fact]
        public async Task TrySyncTriggers_CheckHash_PostsExpectedContent()
        {
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var hashBlob = _functionsSyncManager.GetHashBlob();
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
                Assert.True(logMessages[0].StartsWith("SyncTriggers content: {"));
                var idx = logMessages[0].IndexOf('{');
                var sanitizedContent = logMessages[0].Substring(idx);
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
                var hashBlob = _functionsSyncManager.GetHashBlob();
                await hashBlob.DeleteIfExistsAsync();

                _mockHttpHandler.MockStatusCode = HttpStatusCode.InternalServerError;
                var syncResult = await _functionsSyncManager.TrySyncTriggersAsync(checkHash: true);
                Assert.False(syncResult.Success);
                string expectedErrorMessage = "SyncTriggers call failed. StatusCode=InternalServerError";
                Assert.Equal(expectedErrorMessage, syncResult.Error);
                Assert.Equal(1, _mockHttpHandler.RequestCount);
                var result = JObject.Parse(_contentBuilder.ToString());
                var triggers = result["triggers"];
                Assert.Equal(_expectedSyncTriggersPayload, triggers.ToString(Formatting.None));
                bool hashBlobExists = await hashBlob.ExistsAsync();
                Assert.False(hashBlobExists);

                // verify log statements
                var logMessages = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
                Assert.True(logMessages[0].StartsWith("SyncTriggers content: {"));
                Assert.Equal(expectedErrorMessage, logMessages[1]);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("notaconnectionstring")]
        public void CheckAndUpdateHashAsync_ReturnsExpectedValue(string storageConnectionString)
        {
            _vars.Add("AzureWebJobsStorage", storageConnectionString);
            using (var env = new TestScopedEnvironmentVariable(_vars))
            {
                var blob = _functionsSyncManager.GetHashBlob();
                Assert.Null(blob);
            }
        }

        [Theory]
        [InlineData(1, "http://sitename/operations/settriggers")]
        [InlineData(0, "https://sitename/operations/settriggers")]
        public void Disables_Ssl_If_SkipSslValidation_Enabled(int skipSslValidation, string syncTriggersUri)
        {
            HostNameProvider.Reset();

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
            HostNameProvider.Reset();

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

        private static IFileSystem CreateFileSystem(ScriptHostConfiguration hostConfig, string hostJsonContent = null)
        {
            string rootScriptPath = hostConfig.RootScriptPath;
            string testDataPath = hostConfig.TestDataPath;

            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, "host.json"))).Returns(true);

            hostJsonContent = hostJsonContent ?? @"{ ""durableTask"": { ""HubName"": ""TestHubValue"", ""azureStorageConnectionStringName"": ""DurableStorage"" }}";
            var testHostJsonStream = new MemoryStream(Encoding.UTF8.GetBytes(hostJsonContent));
            testHostJsonStream.Position = 0;
            fileBase.Setup(f => f.Open(Path.Combine(rootScriptPath, @"host.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(testHostJsonStream);

            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);

            dirBase.Setup(d => d.EnumerateDirectories(rootScriptPath))
                .Returns(new[]
                {
                    Path.Combine(rootScriptPath, "function1"),
                    Path.Combine(rootScriptPath, "function2"),
                    Path.Combine(rootScriptPath, "function3")
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

            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, @"function1\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, @"function1\main.py"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootScriptPath, @"function1\function.json"))).Returns(function1);
            fileBase.Setup(f => f.Open(Path.Combine(rootScriptPath, @"function1\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function1.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
            });

            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, @"function2\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, @"function2\main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootScriptPath, @"function2\function.json"))).Returns(function2);
            fileBase.Setup(f => f.Open(Path.Combine(rootScriptPath, @"function2\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function2));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function2.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
            });

            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, @"function3\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootScriptPath, @"function3\main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootScriptPath, @"function3\function.json"))).Returns(function3);
            fileBase.Setup(f => f.Open(Path.Combine(rootScriptPath, @"function3\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function3));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function3.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
            });

            return fileSystem.Object;
        }

        public void Dispose()
        {
            WebScriptHostManager.ResetStandbyMode();
            ScriptSettingsManager.Instance = new ScriptSettingsManager();

            // Clean up mock IFileSystem
            FileUtility.Instance = null;
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebsiteAuthEncryptionKey, string.Empty);
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