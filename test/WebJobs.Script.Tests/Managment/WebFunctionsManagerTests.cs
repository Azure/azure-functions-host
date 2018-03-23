// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Tests.Helpers;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class WebFunctionsManagerTests : IDisposable
    {
        [Fact]
        public async Task VerifyDurableTaskHubNameIsAdded()
        {
            // Setup
            var settings = CreateWebSettings();
            var fileSystem = CreateFileSystem(settings.ScriptPath);
            var loggerFactory = CreateLoggerFactory();
            var webManager = new WebFunctionsManager(settings, loggerFactory);
            var httpClient = CreateHttpClient();

            FileUtility.Instance = fileSystem;
            HttpClientUtility.Instance = httpClient;

            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", SimpleWebTokenTests.GenerateKeyHexString());
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", "appName");

            // Act
            (var success, var error) = await webManager.TrySyncTriggers();

            // Assert
            Assert.True(success, "SyncTriggers should return success true");
            Assert.True(string.IsNullOrEmpty(error), "Error should be null or empty");
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient(new MockHttpHandler());
        }

        private static WebHostSettings CreateWebSettings()
        {
            return new WebHostSettings
            {
                ScriptPath = @"x:\root",
                IsAuthDisabled = false,
                IsSelfHost = false,
                LogPath = @"x:\tmp\log",
                SecretsPath = @"x:\secrets",
                TestDataPath = @"x:\test"
            };
        }

        private static IFileSystem CreateFileSystem(string rootPath)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Is(Path.Combine(rootPath, "host.json"))).Returns(true);

            var hostJson = new MemoryStream(Encoding.UTF8.GetBytes(@"{}"));
            fileSystem.File
                .Open(Arg.Is(Path.Combine(rootPath, @"host.json")), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(hostJson);

            fileSystem.Directory.EnumerateDirectories(Arg.Is(rootPath))
                .Returns(new[]
                {
                    @"x:\root\function1",
                    @"x:\root\function2"
                });

            var function1 = new MemoryStream(Encoding.UTF8.GetBytes(@"{
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
}"));
            var function2 = new MemoryStream(Encoding.UTF8.GetBytes(@"{
  ""disabled"": false,
  ""bindings"": [
    {
      ""name"": ""myQueueItem"",
      ""type"": ""queueTrigger"",
      ""direction"": ""in"",
      ""queueName"": ""myqueue-items"",
      ""connection"": """"
    }
  ]
}"));
            fileSystem.File
                .Open(Arg.Is(Path.Combine(rootPath, @"function1\function.json")), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(function1);

            fileSystem.File
                .Open(Arg.Is(Path.Combine(rootPath, @"function1\function.json")), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(function2);

            return fileSystem;
        }

        private static ILoggerFactory CreateLoggerFactory()
        {
            var loggerFactory = Substitute.For<ILoggerFactory>();
            var logger = CreateLogger();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
            return loggerFactory;
        }

        private static ILogger CreateLogger()
        {
            var logger = Substitute.For<ILogger>();
            logger.Log(Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>());
            return logger;
        }

        public void Dispose()
        {
            // Clean up mock IFileSystem
            FileUtility.Instance = null;
            Environment.SetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY", string.Empty);
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", string.Empty);
            HttpClientUtility.Instance = null;
        }

        private class MockHttpHandler : HttpClientHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }
    }
}
