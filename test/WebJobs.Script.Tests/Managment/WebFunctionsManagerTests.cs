// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class WebFunctionsManagerTests : IDisposable
    {
        private const string TestHostName = "test.azurewebsites.net";

        private readonly string _testRootScriptPath;
        private readonly string _testHostConfigFilePath;
        private readonly ScriptApplicationHostOptions _hostOptions;
        private readonly WebFunctionsManager _webFunctionsManager;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly IFileSystem _fileSystem;

        public WebFunctionsManagerTests()
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

            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample");

            var fileSystem = CreateFileSystem(_hostOptions);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var contentBuilder = new StringBuilder();
            var httpClientFactory = CreateHttpClientFactory(contentBuilder);
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_hostOptions);
            var tokenSource = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var changeTokens = new[] { tokenSource };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);
            var secretManagerProviderMock = new Mock<ISecretManagerProvider>(MockBehavior.Strict);
            var secretManagerMock = new Mock<ISecretManager>(MockBehavior.Strict);
            secretManagerProviderMock.SetupGet(p => p.Current).Returns(secretManagerMock.Object);
            var hostSecretsInfo = new HostSecretsInfo();
            secretManagerMock.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(hostSecretsInfo);
            Dictionary<string, string> functionSecrets = new Dictionary<string, string>();
            secretManagerMock.Setup(p => p.GetFunctionSecretsAsync("httptrigger", false)).ReturnsAsync(functionSecrets);

            var configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            configurationMock.Setup(p => p.GetReloadToken()).Returns(new Mock<IChangeToken>().Object);

            var hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            var mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>(MockBehavior.Strict);
            mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(false);
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(TestHostName);
            var hostNameProvider = new HostNameProvider(_mockEnvironment.Object);

            var workerOptions = new LanguageWorkerOptions();
            FileUtility.Instance = fileSystem;
            _fileSystem = fileSystem;
            var metadataProvider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, new TestMetricsLogger(), SystemEnvironment.Instance);
            var defaultProvider = new FunctionMetadataProvider(NullLogger<FunctionMetadataProvider>.Instance, null, metadataProvider, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()), SystemEnvironment.Instance);
            var functionMetadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions()), defaultProvider, null, new OptionsWrapper<HttpWorkerOptions>(new HttpWorkerOptions()), loggerFactory, new TestOptionsMonitor<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));

            var emptyOptions = new JobHostInternalStorageOptions();
            var azureBlobStorageProvider = TestHelpers.GetAzureBlobStorageProvider(configurationMock.Object, storageOptions: emptyOptions);
            var functionsSyncManager = new FunctionsSyncManager(configurationMock.Object, hostIdProviderMock.Object, optionsMonitor, loggerFactory.CreateLogger<FunctionsSyncManager>(), httpClientFactory, secretManagerProviderMock.Object, mockWebHostEnvironment.Object, _mockEnvironment.Object, hostNameProvider, functionMetadataManager, azureBlobStorageProvider);
            _webFunctionsManager = new WebFunctionsManager(optionsMonitor, loggerFactory, httpClientFactory, secretManagerProviderMock.Object, functionsSyncManager, hostNameProvider, functionMetadataManager);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ReadFunctionsMetadataSucceeds(bool testDataCapEnabled)
        {
            var env = new Dictionary<string, string>();
            if (!testDataCapEnabled)
            {
                env.Add(EnvironmentSettingNames.TestDataCapEnabled, "0");
            }
            var envScope = new TestScopedEnvironmentVariable(env);
            using (envScope)
            {
                var metadata = (await _webFunctionsManager.GetFunctionsMetadata(includeProxies: false)).ToArray();

                Assert.Equal(2, metadata.Count(p => p.Language == RpcWorkerConstants.NodeLanguageWorkerName));
                Assert.Equal(1, metadata.Count(p => string.IsNullOrEmpty(p.Language)));

                Assert.Equal("https://test.azurewebsites.net/admin/vfs/function1.dat", metadata[0].TestDataHref.AbsoluteUri);
                Assert.Equal("https://test.azurewebsites.net/admin/vfs/function2.dat", metadata[1].TestDataHref.AbsoluteUri);
                Assert.Equal("https://test.azurewebsites.net/admin/vfs/function3.dat", metadata[2].TestDataHref.AbsoluteUri);

                Assert.Equal("Test Data 1", metadata[0].TestData);
                Assert.Equal("Test Data 2", metadata[1].TestData);

                if (testDataCapEnabled)
                {
                    // TestData size is capped by default
                    Assert.Null(metadata[2].TestData);
                }
                else
                {
                    Assert.Equal(ScriptConstants.MaxTestDataInlineStringLength + 1, metadata[2].TestData.Length);
                }
            }
        }

        [Fact]
        public async Task TryGetFunction_NoMatchingFunction_ReturnsEmpty()
        {
            var result = await _webFunctionsManager.TryGetFunction("non-function", null);
            Assert.False(result.Success);
        }

        [Fact]
        public async Task TryGetFunction_NoFunction_ReturnsEmpty()
        {
            try
            {
                FileUtility.Instance = CreateEmptyFileSystem(_hostOptions);
                var action = await _webFunctionsManager.TryGetFunction("function1", null);
                Assert.False(action.Success);
            }
            finally
            {
                FileUtility.Instance = _fileSystem;
            }
        }

        [Theory]
        [InlineData(null, "api")]
        [InlineData("", "api")]
        [InlineData("this { not json", "api")]
        [InlineData("{}", "api")]
        [InlineData("{ extensions: {} }", "api")]
        [InlineData("{ extensions: { http: {} }", "api")]
        [InlineData("{ extensions: { http: { routePrefix: 'test' }, foo: {} } }", "test")]
        public async Task GetRoutePrefix_Succeeds(string content, string expected)
        {
            if (content != null)
            {
                File.WriteAllText(_testHostConfigFilePath, content);
            }

            string prefix = await WebFunctionsManager.GetRoutePrefix(_testRootScriptPath);
            Assert.Equal(expected, prefix);
        }

        [Theory]
        [InlineData(null, null, "https://localhost")]
        [InlineData(null, "testhost", "https://testhost.azurewebsites.net")]
        [InlineData("testhost.foo.com", null, "https://testhost.foo.com")]
        public void GetBaseUrl_ReturnsExpectedValue(string hostName, string siteName, string expected)
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(hostName);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns(siteName);
            Assert.Equal(expected, _webFunctionsManager.GetBaseUrl());
        }

        private static IHttpClientFactory CreateHttpClientFactory(StringBuilder writeContent)
        {
            var httpClient = new HttpClient(new MockHttpHandler(writeContent));
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(m => m.CreateClient(It.IsAny<string>()))
                 .Returns(httpClient);
            return mockFactory.Object;
        }

        private static LanguageWorkerOptions CreateLanguageWorkerConfigSettings()
        {
            return new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
        }

        private static IFileSystem CreateEmptyFileSystem(ScriptApplicationHostOptions options)
        {
            string rootPath = options.ScriptPath;
            string testDataPath = options.TestDataPath;

            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, "host.json"))).Returns(true);

            var hostJson = new MemoryStream(Encoding.UTF8.GetBytes(@"{ ""durableTask"": { ""HubName"": ""TestHubValue"", ""azureStorageConnectionStringName"": ""DurableStorage"" }}"));
            hostJson.Position = 0;
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"host.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(hostJson);

            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);

            dirBase.Setup(d => d.Exists(options.ScriptPath)).Returns(true);
            dirBase.Setup(d => d.EnumerateDirectories(rootPath)).Returns(new string[0]);
            return fileSystem.Object;
        }

        private static IFileSystem CreateFileSystem(ScriptApplicationHostOptions options)
        {
            string rootPath = options.ScriptPath;
            string testDataPath = options.TestDataPath;

            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var dirBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, "host.json"))).Returns(true);

            var hostJson = new MemoryStream(Encoding.UTF8.GetBytes(@"{ ""durableTask"": { ""HubName"": ""TestHubValue"", ""azureStorageConnectionStringName"": ""DurableStorage"" }}"));
            hostJson.Position = 0;
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"host.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(hostJson);

            fileSystem.SetupGet(f => f.Directory).Returns(dirBase.Object);

            dirBase.Setup(d => d.Exists(options.ScriptPath)).Returns(true);
            dirBase.Setup(d => d.EnumerateDirectories(rootPath))
                .Returns(new[]
                {
                    Path.Combine(rootPath, "function1"),
                    Path.Combine(rootPath, "function2"),
                    Path.Combine(rootPath, "function3")
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

            string testData1 = "Test Data 1";
            string testData2 = "Test Data 2";
            string testData3 = TestHelpers.NewRandomString(ScriptConstants.MaxTestDataInlineStringLength + 1);

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1", "function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1", "main.py"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function1", "function.json"))).Returns(function1);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function1", "function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function1.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(testData1));
            });

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function2", "function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function2", "main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function2", "function.json"))).Returns(function2);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function2", "function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function2));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function2.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(testData2));
            });

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function3", "function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function3", "main.js"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function3", "function.json"))).Returns(function3);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function3", "function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function3));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function3.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(testData3));
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
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _content.Append(await request.Content.ReadAsStringAsync());
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }
        }
    }
}