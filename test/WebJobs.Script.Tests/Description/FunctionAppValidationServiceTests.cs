// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionAppValidationServiceTests
    {
        private readonly ILogger _testLogger;
        private readonly Mock<IOptions<ScriptJobHostOptions>> _scriptOptionsMock;
        private readonly ScriptJobHostOptions _scriptJobHostOptions;
        private readonly TestLoggerProvider _testLoggerProvider;
        private readonly ILoggerFactory _loggerFactory;

        public FunctionAppValidationServiceTests()
        {
            _scriptJobHostOptions = new ScriptJobHostOptions
            {
                RootScriptPath = "test-root-path",
                IsDefaultHostConfig = false
            };

            _scriptOptionsMock = new Mock<IOptions<ScriptJobHostOptions>>();
            _scriptOptionsMock.Setup(o => o.Value).Returns(_scriptJobHostOptions);

            _testLoggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_testLoggerProvider);
            _testLogger = _loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        [Fact]
        public async Task StartAsync_NotDotnetIsolatedApp_DoesNotLogError()
        {
            _testLoggerProvider.ClearAllLogMessages();

            var mockValidator = new Mock<IFunctionAppValidator>();
            // No-op validator
            var service = new FunctionAppValidationService(
                _loggerFactory,
                _scriptOptionsMock.Object,
                new TestEnvironment(),
                [mockValidator.Object]);

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => string.Equals(val.EventId.Name, "MissingAzureFunctionsFolder"));

            Assert.Null(traceMessage);
        }

        [Fact]
        public async Task StartAsync_PlaceholderMode_DoesNotLogError()
        {
            _testLoggerProvider.ClearAllLogMessages();

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var mockValidator = new Mock<IFunctionAppValidator>();
            var service = new FunctionAppValidationService(
                _loggerFactory,
                _scriptOptionsMock.Object,
                environment,
                [mockValidator.Object]);

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => string.Equals(val.EventId.Name, "MissingAzureFunctionsFolder"));

            Assert.Null(traceMessage);
        }

        [Fact]
        public async Task StartAsync_NewAppWithNoPayload_DoesNotLogError()
        {
            _testLoggerProvider.ClearAllLogMessages();

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");

            var scriptOptionsMock = new Mock<IOptions<ScriptJobHostOptions>>();

            var scriptJobHostOptions = new ScriptJobHostOptions
            {
                RootScriptPath = "test-root-path",
                IsDefaultHostConfig = true
            };

            scriptOptionsMock.Setup(o => o.Value).Returns(scriptJobHostOptions);

            var mockValidator = new Mock<IFunctionAppValidator>();
            var service = new FunctionAppValidationService(
                _loggerFactory,
                scriptOptionsMock.Object,
                environment,
                [mockValidator.Object]);

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => string.Equals(val.EventId.Name, "MissingAzureFunctionsFolder"));

            Assert.Null(traceMessage);
        }

        [Fact]
        public async Task StartAsync_MissingAzureFunctionsFolder_LogsWarning()
        {
            _testLoggerProvider.ClearAllLogMessages();

            string path = "test-root-path";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var functionMetadataList = ImmutableArray.Create(new FunctionMetadata());

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");

            // Use the real validator for folder check
            var folderValidator = new MissingAzureFunctionsFolderValidator();
            var service = new FunctionAppValidationService(
                _loggerFactory,
                _scriptOptionsMock.Object,
                environment,
                [folderValidator]);

            // Act
            await service.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() =>
            {
                int completed = _testLoggerProvider.GetAllLogMessages().Count(p => p.FormattedMessage.Contains("Could not find the .azurefunctions folder in the deployed artifacts of a .NET isolated function app."));
                return completed > 0;
            });
        }

        [Theory]
        [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "3.36.0", true)]
        [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "2.25.0", true)]
        [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "4.22.0", false)]
        [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Preview", "4.29.0", false)]
        [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Preview", "3.2.0", false)]
        [InlineData(null, null, false)]
        [InlineData("", "", false)]
        public void GetOutdatedBundleWarningMessage_LogsWarning(string bundleId, string bundleVersion, bool shouldLogEvent)
        {
            // Arrange
            _testLoggerProvider.ClearAllLogMessages();

            var options = new ExtensionBundleOptions { Id = bundleId };
            var env = new TestEnvironment();
            var config = new FunctionsHostingConfigOptions();
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
            var manager = new ExtensionBundleManager(options, env, _loggerFactory, config, httpClientFactory.Object);

            // Set the private _extensionBundleVersion field using reflection
            typeof(ExtensionBundleManager)
                .GetField("_extensionBundleVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(manager, bundleVersion);

            var validator = new ExtensionBundleManagerValidator(manager);
            var service = new FunctionAppValidationService(
                _loggerFactory,
                _scriptOptionsMock.Object,
                env,
                [validator]);

            // Act
            validator.Validate(_scriptJobHostOptions, env, _testLogger);

            // Assert
            var logMessages = _testLoggerProvider.GetAllLogMessages();

            bool hasOutdatedBundleLog = logMessages.Any(m => m.FormattedMessage.Contains(bundleVersion)
                && m.FormattedMessage.Contains("deprecated version")
                && m.Level == LogLevel.Warning);

            Assert.Equal(shouldLogEvent, hasOutdatedBundleLog);
        }
    }
}