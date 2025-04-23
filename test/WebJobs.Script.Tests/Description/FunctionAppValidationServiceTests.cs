using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class FunctionAppValidationServiceTests
    {
        private readonly ILogger<FunctionAppValidationService> _testLogger;
        private readonly Mock<IOptions<ScriptJobHostOptions>> _scriptOptionsMock;
        private readonly ScriptJobHostOptions _scriptJobHostOptions;
        private readonly TestLoggerProvider _testLoggerProvider;

        public FunctionAppValidationServiceTests()
        {
            _scriptOptionsMock = new Mock<IOptions<ScriptJobHostOptions>>();

            _scriptJobHostOptions = new ScriptJobHostOptions
            {
                RootScriptPath = "test-root-path",
                IsDefaultHostConfig = false
            };

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

            var service = new FunctionAppValidationService(
                _testLogger,
                _scriptOptionsMock.Object);

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

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "dotnet-isolated");

            var service = new FunctionAppValidationService(
                _testLogger,
                _scriptOptionsMock.Object);

            // Act
            await service.StartAsync(CancellationToken.None);

            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, null);

            //Assert
            var traces = _testLoggerProvider.GetAllLogMessages();
            var traceMessage = traces.FirstOrDefault(val => val.EventId.Name.Equals("NoAzureFunctionsFolder"));

            Assert.NotNull(traceMessage);
        }
    }
}
