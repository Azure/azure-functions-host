// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class FunctionAppValidationServiceTests
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
        private readonly Mock<IServiceScope> _serviceScopeMock;
        private readonly Mock<IOptions<ScriptJobHostOptions>> _scriptOptionsMock;
        private readonly Mock<IFunctionMetadataManager> _functionMetadataManagerMock;
        private readonly ScriptJobHostOptions _scriptJobHostOptions;
        private readonly ILogger<FunctionAppValidationService> _testLogger;
        private readonly TestLoggerProvider _testLoggerProvider;

        public FunctionAppValidationServiceTests()
        {
            _serviceProviderMock = new Mock<IServiceProvider>();
            _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
            _serviceScopeMock = new Mock<IServiceScope>();
            _scriptOptionsMock = new Mock<IOptions<ScriptJobHostOptions>>();
            _functionMetadataManagerMock = new Mock<IFunctionMetadataManager>();

            _scriptJobHostOptions = new ScriptJobHostOptions
            {
                RootScriptPath = "test-root-path",
                IsDefaultHostConfig = false
            };

            _scriptOptionsMock.Setup(o => o.Value).Returns(_scriptJobHostOptions);

            // Setup the service scope
            _serviceScopeMock
                .Setup(s => s.ServiceProvider)
                .Returns(_serviceProviderMock.Object);

            _serviceScopeFactoryMock
                .Setup(f => f.CreateScope())
                .Returns(_serviceScopeMock.Object);

            // Ensure the service provider returns the scope factory
            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
                .Returns(_serviceScopeFactoryMock.Object);

            _scriptOptionsMock.Setup(o => o.Value).Returns(_scriptJobHostOptions);

            _testLoggerProvider = new TestLoggerProvider();
            LoggerFactory factory = new LoggerFactory();
            factory.AddProvider(_testLoggerProvider);
            _testLogger = factory.CreateLogger<FunctionAppValidationService>();
        }

        [Fact]
        public async Task StartAsync_FunctionMetadataListIsEmpty_DoesNotLogError()
        {
            _testLoggerProvider.ClearAllLogMessages();

            // Arrange
            _functionMetadataManagerMock
                .Setup(m => m.GetFunctionMetadata(true, true, true))
                .Returns(ImmutableArray<FunctionMetadata>.Empty);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IFunctionMetadataManager)))
                .Returns(_functionMetadataManagerMock.Object);

            var service = new FunctionAppValidationService(
                _serviceProviderMock.Object,
                _testLogger,
                _scriptOptionsMock.Object,
                SystemEnvironment.Instance);

            // Act
            await service.StartAsync(CancellationToken.None);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("NoAzureFunctionsFolder"));

            Assert.Null(traceMessage);
        }

        [Fact]
        public async Task StartAsync_NoAzureFunctionsFolder_LogsWarning()
        {
            _testLoggerProvider.ClearAllLogMessages();

            // Arrange
            var functionMetadataList = ImmutableArray.Create(new FunctionMetadata());

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");

            // Arrange
            _functionMetadataManagerMock
                .Setup(m => m.GetFunctionMetadata(true, true, true))
                .Returns(functionMetadataList);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IFunctionMetadataManager)))
                .Returns(_functionMetadataManagerMock.Object);

            var service = new FunctionAppValidationService(
                _serviceProviderMock.Object,
                _testLogger,
                _scriptOptionsMock.Object,
                environment);

            // Act
            await service.StartAsync(CancellationToken.None);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, null);

            Task.Delay(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult();

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("NoAzureFunctionsFolder"));

            Assert.NotNull(traceMessage);
        }
    }
}
