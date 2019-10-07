// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
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

            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample");

            var fileSystem = CreateFileSystem(_hostOptions);
            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();
            var contentBuilder = new StringBuilder();
            var httpClient = CreateHttpClient(contentBuilder);
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(_hostOptions);
            var tokenSource = new TestChangeTokenSource();
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
            var hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            var mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>(MockBehavior.Strict);
            mockWebHostEnvironment.SetupGet(p => p.InStandbyMode).Returns(false);
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady)).Returns("1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.CoreToolsEnvironment)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(TestHostName);
            var hostNameProvider = new HostNameProvider(_mockEnvironment.Object, loggerFactory.CreateLogger<HostNameProvider>());

            var workerOptions = new LanguageWorkerOptions();
            FileUtility.Instance = fileSystem;
            var languageWorkerOptions = new OptionsWrapper<LanguageWorkerOptions>(CreateLanguageWorkerConfigSettings());
            var metadataProvider = new FunctionMetadataProvider(optionsMonitor, languageWorkerOptions, NullLogger<FunctionMetadataProvider>.Instance);
            var functionsSyncManager = new FunctionsSyncManager(configurationMock.Object, hostIdProviderMock.Object, optionsMonitor, languageWorkerOptions, loggerFactory.CreateLogger<FunctionsSyncManager>(), httpClient, secretManagerProviderMock.Object, mockWebHostEnvironment.Object, _mockEnvironment.Object, hostNameProvider, metadataProvider);
            _webFunctionsManager = new WebFunctionsManager(optionsMonitor, new OptionsWrapper<LanguageWorkerOptions>(CreateLanguageWorkerConfigSettings()), loggerFactory, httpClient, secretManagerProviderMock.Object, functionsSyncManager, hostNameProvider, metadataProvider);
        }

        [Fact]
        public async Task ReadFunctionsMetadataSucceeds()
        {
            IEnumerable<FunctionMetadataResponse> metadata = await _webFunctionsManager.GetFunctionsMetadata(includeProxies: false);
            var jsFunctions = metadata.Where(funcMetadata => funcMetadata.Language == LanguageWorkerConstants.NodeLanguageWorkerName).ToList();
            var unknownFunctions = metadata.Where(funcMetadata => string.IsNullOrEmpty(funcMetadata.Language)).ToList();

            Assert.Equal(2, jsFunctions.Count());
            Assert.Equal(1, unknownFunctions.Count());
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

        [Fact]
        public void GetFunctionInvokeUrlTemplate_ReturnsExpectedResult()
        {
            string baseUrl = "https://localhost";
            var functionMetadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            var httpTriggerBinding = new BindingMetadata
            {
                Name = "req",
                Type = "httpTrigger",
                Direction = BindingDirection.In,
                Raw = new JObject()
            };
            functionMetadata.Bindings.Add(httpTriggerBinding);
            var uri = FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, "api");
            Assert.Equal("https://localhost/api/testfunction", uri.ToString());

            // with empty route prefix
            uri = FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, string.Empty);
            Assert.Equal("https://localhost/testfunction", uri.ToString());

            // with a custom route
            httpTriggerBinding.Raw.Add("route", "catalog/products/{category:alpha?}/{id:int?}");
            uri = FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, "api");
            Assert.Equal("https://localhost/api/catalog/products/{category:alpha?}/{id:int?}", uri.ToString());

            // with empty route prefix
            uri = FunctionMetadataExtensions.GetFunctionInvokeUrlTemplate(baseUrl, functionMetadata, string.Empty);
            Assert.Equal("https://localhost/catalog/products/{category:alpha?}/{id:int?}", uri.ToString());
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

        private static HttpClient CreateHttpClient(StringBuilder writeContent)
        {
            return new HttpClient(new MockHttpHandler(writeContent));
        }

        private static LanguageWorkerOptions CreateLanguageWorkerConfigSettings()
        {
            return new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
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

            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1\function.json"))).Returns(true);
            fileBase.Setup(f => f.Exists(Path.Combine(rootPath, @"function1\main.py"))).Returns(true);
            fileBase.Setup(f => f.ReadAllText(Path.Combine(rootPath, @"function1\function.json"))).Returns(function1);
            fileBase.Setup(f => f.Open(Path.Combine(rootPath, @"function1\function.json"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
            });
            fileBase.Setup(f => f.Open(Path.Combine(testDataPath, "function1.dat"), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>())).Returns(() =>
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
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
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
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
                return new MemoryStream(Encoding.UTF8.GetBytes(function1));
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