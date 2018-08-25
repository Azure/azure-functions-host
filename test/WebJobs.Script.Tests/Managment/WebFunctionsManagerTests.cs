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
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class WebFunctionsManagerTests : IDisposable
    {
        [Fact]
        public async Task VerifyDurableTaskHubNameIsAdded()
        {
            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.WebSiteAuthEncryptionKey, TestHelpers.GenerateKeyHexString() },
                { EnvironmentSettingNames.AzureWebsiteHostName, "appName.azurewebsites.net" }
            };
            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // Setup
                const string expectedSyncTriggersPayload = "[{\"authLevel\":\"anonymous\",\"type\":\"httpTrigger\",\"direction\":\"in\",\"name\":\"req\",\"functionName\":\"function1\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"orchestrationTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function2\",\"taskHubName\":\"TestHubValue\"}," +
                "{\"name\":\"myQueueItem\",\"type\":\"activityTrigger\",\"direction\":\"in\",\"queueName\":\"myqueue-items\",\"connection\":\"DurableStorage\",\"functionName\":\"function3\",\"taskHubName\":\"TestHubValue\"}]";
                var settings = CreateWebSettings();
                var fileSystem = CreateFileSystem(settings.ScriptPath);
                var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
                var contentBuilder = new StringBuilder();
                var httpClient = CreateHttpClient(contentBuilder);
                var webManager = new WebFunctionsManager(new OptionsWrapper<ScriptApplicationHostOptions>(settings), new OptionsWrapper<LanguageWorkerOptions>(CreateLanguageWorkerConfigSettings()), loggerFactory, httpClient);

                FileUtility.Instance = fileSystem;

                // Act
                (var success, var error) = await webManager.TrySyncTriggers();
                var content = contentBuilder.ToString();

                // Assert
                Assert.True(success, "SyncTriggers should return success true");
                Assert.True(string.IsNullOrEmpty(error), "Error should be null or empty");
                Assert.Equal(expectedSyncTriggersPayload, content);
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
                var httpRequest = WebFunctionsManager.BuildSyncTriggersRequest();
                Assert.Equal(syncTriggersUri, httpRequest.RequestUri.AbsoluteUri);
                Assert.Equal(HttpMethod.Post, httpRequest.Method);
            }
        }

        [Fact]
        public static void ReadFunctionsMetadataSucceeds()
        {
            string functionsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample");
            // Setup
            var settings = CreateWebSettings();
            var fileSystem = CreateFileSystem(settings.ScriptPath);
            var loggerFactory = MockNullLogerFactory.CreateLoggerFactory();
            var contentBuilder = new StringBuilder();
            var httpClient = CreateHttpClient(contentBuilder);
            var webManager = new WebFunctionsManager(new OptionsWrapper<ScriptApplicationHostOptions>(settings), new OptionsWrapper<LanguageWorkerOptions>(CreateLanguageWorkerConfigSettings()), loggerFactory, httpClient);

            FileUtility.Instance = fileSystem;
            IEnumerable<FunctionMetadata> metadata = webManager.GetFunctionsMetadata();
            var jsFunctions = metadata.Where(funcMetadata => funcMetadata.Language == LanguageWorkerConstants.NodeLanguageWorkerName).ToList();
            var unknownFunctions = metadata.Where(funcMetadata => string.IsNullOrEmpty(funcMetadata.Language)).ToList();

            Assert.Equal(2, jsFunctions.Count());
            Assert.Equal(1, unknownFunctions.Count());
        }

        private static HttpClient CreateHttpClient(StringBuilder writeContent)
        {
            return new HttpClient(new MockHttpHandler(writeContent));
        }

        private static ScriptApplicationHostOptions CreateWebSettings()
        {
            return new ScriptApplicationHostOptions
            {
                ScriptPath = @"x:\root",
                IsSelfHost = false,
                LogPath = @"x:\tmp\log",
                SecretsPath = @"x:\secrets",
                TestDataPath = @"x:\test"
            };
        }

        private static LanguageWorkerOptions CreateLanguageWorkerConfigSettings()
        {
            return new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
        }

        private static IFileSystem CreateFileSystem(string rootPath)
        {
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
