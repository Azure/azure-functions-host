// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Tests.Security;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Security.Utilities;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DefaultScriptWebHookProviderTests
    {
        private const string TestHostName = "test.azurewebsites.net";
        private const string TestUrlRoot = $"https://{TestHostName}/runtime/webhooks/testextension?code=";

        private static DefaultScriptWebHookProvider CreateDefaultScriptWebHookProvider(out Mock<ISecretManager> mockSecretManager, out HostSecretsInfo hostSecrets)
        {
            mockSecretManager = new Mock<ISecretManager>(MockBehavior.Strict);
            hostSecrets = new HostSecretsInfo();
            hostSecrets.SystemKeys = new Dictionary<string, string>();
            hostSecrets.FunctionKeys = new Dictionary<string, string>();
            var mockSecretManagerProvider = new Mock<ISecretManagerProvider>(MockBehavior.Strict);
            mockSecretManagerProvider.Setup(p => p.Current).Returns(mockSecretManager.Object);
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, TestHostName);
            var hostNameProvider = new HostNameProvider(testEnvironment);
            return new DefaultScriptWebHookProvider(mockSecretManagerProvider.Object, hostNameProvider);
        }

        [Fact]
        public void GetUrl_ReturnsExpectedResult()
        {
            var webHookProvider = CreateDefaultScriptWebHookProvider(out Mock<ISecretManager> mockSecretManager, out HostSecretsInfo hostSecrets);
            mockSecretManager.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(hostSecrets);

            // When an extension has an existing secret, it should be returned.
            hostSecrets.SystemKeys = new Dictionary<string, string>
            {
                { "testextension_extension", "abc123" }
            };

            var configProvider = new TestExtensionConfigProvider();
            var url = webHookProvider.GetUrl(configProvider);
            Assert.Equal($"{TestUrlRoot}abc123", url.ToString());
        }

        [Fact]
        public void GetUrl_GeneratesIdentifiableSystemSecret()
        {
            string secretValue = string.Empty;

            var webHookProvider = CreateDefaultScriptWebHookProvider(out Mock<ISecretManager> mockSecretManager, out HostSecretsInfo hostSecrets);
            mockSecretManager.Setup(p => p.GetHostSecretsAsync()).ReturnsAsync(hostSecrets);

            mockSecretManager.Setup(p =>
                p.AddOrUpdateFunctionSecretAsync(
                    "testextension_extension",
                    It.IsAny<string>(),
                    HostKeyScopes.SystemKeys,
                    ScriptSecretsType.Host))
                        .Callback<string, string, string, ScriptSecretsType>((key, secret, scope, type) => secretValue = secret)
                        .Returns(() => Task.FromResult(new KeyOperationResult(secretValue, OperationResult.Created)));

            // When an extension has no existing secret, one should be generated using
            // the Azure Functions system key seed and standard fixed signature.

            var configProvider = new TestExtensionConfigProvider();
            var url = webHookProvider.GetUrl(configProvider);
            Assert.Equal($"{TestUrlRoot}{secretValue}", url.ToString());
            Assert.True(IdentifiableSecrets.ValidateBase64Key(secretValue,
                                                              SecretGenerator.SystemKeySeed,
                                                              SecretGenerator.AzureFunctionsSignature,
                                                              encodeForUrl: true));
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
