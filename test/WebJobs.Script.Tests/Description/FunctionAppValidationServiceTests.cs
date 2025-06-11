// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
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
        private readonly ILogger<FunctionAppValidationService> _testLogger;
        private readonly Mock<IOptions<ScriptJobHostOptions>> _scriptOptionsMock;
        private readonly ScriptJobHostOptions _scriptJobHostOptions;
        private readonly TestLoggerProvider _testLoggerProvider;

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
            var factory = new LoggerFactory();
            factory.AddProvider(_testLoggerProvider);
            _testLogger = factory.CreateLogger<FunctionAppValidationService>();
        }

        [Fact]
        public async Task StartAsync_NotDotnetIsolatedApp_DoesNotLogError()
        {
            _testLoggerProvider.ClearAllLogMessages();

            var service = new FunctionAppValidationService(
                _testLogger,
                _scriptOptionsMock.Object,
                new TestEnvironment());

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("MissingAzureFunctionsFolder"));

            Assert.Null(traceMessage);
        }

        [Fact]
        public async Task StartAsync_PlaceholderMode_DoesNotLogError()
        {
            _testLoggerProvider.ClearAllLogMessages();

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var service = new FunctionAppValidationService(
                _testLogger,
                _scriptOptionsMock.Object,
                environment);

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("MissingAzureFunctionsFolder"));

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

            var service = new FunctionAppValidationService(
                _testLogger,
                scriptOptionsMock.Object,
                environment);

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("MissingAzureFunctionsFolder"));

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

            var service = new FunctionAppValidationService(
                _testLogger,
                _scriptOptionsMock.Object,
                environment);

            // Act
            await service.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() =>
            {
                int completed = _testLoggerProvider.GetAllLogMessages().Count(p => p.FormattedMessage.Contains("Could not find the .azurefunctions folder in the deployed artifacts of a .NET isolated function app."));
                return completed > 0;
            });
        }
    }
}