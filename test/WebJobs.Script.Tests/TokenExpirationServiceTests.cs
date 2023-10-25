// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Health;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TokenExpirationServiceTests
    {
        private readonly TimeSpan _testDueTime = TimeSpan.FromSeconds(3);
        private TestLoggerProvider _loggerProvider;
        private ILogger<TokenExpirationService> _logger;
        private Mock<IEnvironment> _mockEnvironment;
        private TestOptionsMonitor<StandbyOptions> _optionsMonitor;

        public TokenExpirationServiceTests()
        {
            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _logger = loggerFactory.CreateLogger<TokenExpirationService>();

            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Loose);
        }

        [Theory]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&se=2023-07-20T05:27:05Z&srt=s&ss=bf&sp=rwl", true, true)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&srt=s&ss=bf&sp=rwl", true, false)]
        [InlineData("https://storage.blob.core.windows.net/functions/func.zip", false, false)]
        [InlineData("https://storage.blob.core.windows.net/func/func.zip?sp=r&st=2023-07-12T21:27:05Z&se=2023-07-20T05:27:05Z&spr=https&sv=2022-11-02&sr=b&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D", false, true)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&se=9999-07-20T05:27:05Z&srt=s&ss=bf&sp=rwl", true, true)]
        [InlineData("UseDevelopmentStorage=true", true, false)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;TableEndpoint=https://table.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&srt=s&ss=bf&sp=rwl", true, false)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;TableEndpoint=https://table.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&se=2023-07-20T05:27:05Z&srt=s&ss=bf&sp=rwl", true, true)]
        [InlineData("1", false, false)]
        public async Task StartAsync_Tests(string input, bool isAzureWebJobsStorage, bool shouldEmitEvent)
        {
            var options = new StandbyOptions { InStandbyMode = true };
            _optionsMonitor = new TestOptionsMonitor<StandbyOptions>(options);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorage + "__accountName")).Returns(string.Empty);
            if (isAzureWebJobsStorage)
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorage)).Returns(input);
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage)).Returns(string.Empty);
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString)).Returns(string.Empty);
            }
            else
            {
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorage)).Returns(string.Empty);
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteRunFromPackage)).Returns(input);
                _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString)).Returns(string.Empty);
            }

            using (var provider = new TokenExpirationService(_mockEnvironment.Object, _logger, _optionsMonitor))
            {
                await provider.StartAsync(CancellationToken.None);

                options.InStandbyMode = false;
                _optionsMonitor.InvokeChanged();

                await Task.Delay(2 * _testDueTime);
            }

            var logMessages = _loggerProvider.GetLog();
            if (shouldEmitEvent)
            {
                Assert.True(logMessages.Contains("SAS token within"));
            }
            else
            {
                Assert.False(logMessages.Contains("SAS token within"));
            }
            _loggerProvider.ClearAllLogMessages();
        }

        [Fact]
        public async Task StartAsync_Identities_Test()
        {
            var options = new StandbyOptions { InStandbyMode = true };
            _optionsMonitor = new TestOptionsMonitor<StandbyOptions>(options);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorage + "__accountName")).Returns("accountName");

            using (var provider = new TokenExpirationService(_mockEnvironment.Object, _logger, _optionsMonitor))
            {
                await provider.StartAsync(CancellationToken.None);

                options.InStandbyMode = false;
                _optionsMonitor.InvokeChanged();

                await Task.Delay(2 * _testDueTime);
            }

            var logMessages = _loggerProvider.GetLog();
            Assert.False(logMessages.Contains("SAS token within"));
        }
    }
}
