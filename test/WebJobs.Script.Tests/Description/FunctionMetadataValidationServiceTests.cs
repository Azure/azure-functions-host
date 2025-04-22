using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace WebJobs.Script.Tests
{
    public class FunctionMetadataValidationServiceTests
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
        private readonly Mock<IServiceScope> _serviceScopeMock;
        private readonly Mock<ILogger<FunctionMetadataValidationService>> _loggerMock;
        private readonly Mock<IOptions<ScriptJobHostOptions>> _scriptOptionsMock;
        private readonly Mock<IFunctionMetadataManager> _functionMetadataManagerMock;
        private readonly ScriptJobHostOptions _scriptJobHostOptions;

        public FunctionMetadataValidationServiceTests()
        {
            _serviceProviderMock = new Mock<IServiceProvider>();
            _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
            _serviceScopeMock = new Mock<IServiceScope>();
            _loggerMock = new Mock<ILogger<FunctionMetadataValidationService>>();
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
        }

        [Fact]
        public async Task StartAsync_FunctionMetadataManagerIsNull_DoesNotThrow()
        {
            var service = new FunctionMetadataValidationService(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _scriptOptionsMock.Object);

            // Act & Assert
            await service.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StartAsync_FunctionMetadataListIsEmpty_DoesNotLogError()
        {
            // Arrange
            _functionMetadataManagerMock
                .Setup(m => m.GetFunctionMetadata(true, true, true))
                .Returns(ImmutableArray<FunctionMetadata>.Empty);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IFunctionMetadataManager)))
                .Returns(_functionMetadataManagerMock.Object);

            var service = new FunctionMetadataValidationService(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _scriptOptionsMock.Object);

            // Act
            await service.StartAsync(CancellationToken.None);

            // Assert
            _loggerMock.Verify(
                l => l.Log(
                    It.Is<LogLevel>(level => level == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        /*
        [Fact]
        public async Task StartAsync_NoAzureFunctionsFolder_LogsWarning()
        {
            // Arrange
            var functionMetadataList = ImmutableArray.Create(new FunctionMetadata());

            _functionMetadataManagerMock
                .Setup(m => m.GetFunctionMetadata(true, true, true))
                .Returns(functionMetadataList);

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IFunctionMetadataManager)))
                .Returns(_functionMetadataManagerMock.Object);

            var service = new FunctionMetadataValidationService(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _scriptOptionsMock.Object);

            // Act
            await service.StartAsync(CancellationToken.None);

            // Assert
            _loggerMock.Verify(
                l => l.Log(
                    It.Is<LogLevel>(level => level == LogLevel.Warning),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
        */
    }
}
