// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DefaultScriptWebHookProviderTests
    {
        private const string TestHostName = "test.azurewebsites.net";

        private readonly HostSecretsInfo _hostSecrets;
        private readonly Mock<ISecretManager> _mockSecretManager;
        private readonly IScriptWebHookProvider _webHookProvider;

        public DefaultScriptWebHookProviderTests()
        {
            _mockSecretManager = new Mock<ISecretManager>(MockBehavior.Strict);
            _hostSecrets = new HostSecretsInfo();
            _mockSecretManager.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(_hostSecrets);
            var mockSecretManagerProvider = new Mock<ISecretManagerProvider>(MockBehavior.Strict);
            mockSecretManagerProvider.Setup(p => p.Current).Returns(_mockSecretManager.Object);
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, TestHostName);
            var hostNameProvider = new HostNameProvider(testEnvironment, loggerFactory.CreateLogger<HostNameProvider>());
            _webHookProvider = new DefaultScriptWebHookProvider(mockSecretManagerProvider.Object, hostNameProvider);
        }

        [Fact]
        public void GetUrl_ReturnsExpectedResult()
        {
            _hostSecrets.SystemKeys = new Dictionary<string, string>
            {
                { "testextension_extension", "abc123" }
            };

            var configProvider = new TestExtensionConfigProvider();
            var url = _webHookProvider.GetUrl(configProvider);
            Assert.Equal("https://test.azurewebsites.net/runtime/webhooks/testextension?code=abc123", url.ToString());
        }

        [Extension("My Test Extension", configurationSection: "TestExtension")]
        private class TestExtensionConfigProvider : IExtensionConfigProvider, IAsyncConverter<HttpRequestMessage, HttpResponseMessage>
        {
            public Task<HttpResponseMessage> ConvertAsync(HttpRequestMessage input, CancellationToken cancellationToken)
            {
                throw new System.NotImplementedException();
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }
    }
}
