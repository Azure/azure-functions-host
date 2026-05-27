// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class WebJobsStorageHealthCheckTests
    {
        private readonly Mock<IAzureBlobStorageProvider> _provider = new();
        private readonly TestEnvironment _environment = new();
        private readonly Mock<BlobServiceClient> _mockBlobServiceClient = new();
        private readonly HealthCheckContext _healthCheckContext = new();
        private readonly Mock<IScriptHostManager> _scriptHostManager = new();

        public WebJobsStorageHealthCheckTests()
        {
            _scriptHostManager.SetupGet(m => m.Services).Returns(Mock.Of<IServiceProvider>());
        }

        [Fact]
        public void Constructor_WithNullProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            TestHelpers.Act(() => new WebJobsStorageHealthCheck(null, _environment, _scriptHostManager.Object))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("provider");
        }

        [Fact]
        public void Constructor_WithNullEnvironment_ThrowsArgumentNullException()
        {
            // Act & Assert
            TestHelpers.Act(() => new WebJobsStorageHealthCheck(_provider.Object, null, _scriptHostManager.Object))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("environment");
        }

        [Fact]
        public void Constructor_WithNullScriptHostManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            TestHelpers.Act(() => new WebJobsStorageHealthCheck(_provider.Object, _environment, null))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("scriptHostManager");
        }

        [Fact]
        public async Task CheckHealthAsync_WithNullContext_ThrowsArgumentNullException()
        {
            // Arrange
            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act & Assert
            await TestHelpers.Act(async () => await healthCheck.CheckHealthAsync(null, default))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("context");
        }

        [Fact]
        public async Task CheckHealthAsync_InPlaceholderMode_ReturnsHealthyWithSkippedMessage()
        {
            // Arrange
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            // Assert
            result.Status.Should().Be(HealthStatus.Healthy);
            result.Description.Should().Be("Placeholder mode. Check skipped.");
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_WithNoActiveScriptHost_ReturnsHealthyWithSkippedMessage()
        {
            // Arrange
            _scriptHostManager.SetupGet(m => m.Services).Returns((IServiceProvider)null);
            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            // Assert
            VerifyGetContainersCalled(Times.Never());
            result.Status.Should().Be(HealthStatus.Healthy);
            result.Description.Should().Be("No active script host. Check skipped.");
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_ChecksBlobConnectivity()
        {
            // Arrange
            Page<BlobContainerItem> page = Page<BlobContainerItem>.FromValues([], null, Mock.Of<Response>());
            AsyncPageable<BlobContainerItem> pageable = AsyncPageable<BlobContainerItem>.FromPages([page]);
            SetupGetContainers(pageable);
            BlobServiceClient client = _mockBlobServiceClient.Object;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Returns(true);

            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            // Assert
            VerifyGetContainersCalled(Times.Once());
            result.Status.Should().Be(HealthStatus.Healthy);
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_Twice_ReturnsCachedResult()
        {
            // Arrange
            Page<BlobContainerItem> page = Page<BlobContainerItem>.FromValues([], null, Mock.Of<Response>());
            AsyncPageable<BlobContainerItem> pageable = AsyncPageable<BlobContainerItem>.FromPages([page]);
            SetupGetContainers(pageable);
            BlobServiceClient client = _mockBlobServiceClient.Object;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Returns(true);

            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckContext context = new()
            {
                Registration = new HealthCheckRegistration("test", healthCheck, null, null)
                {
                    Period = Timeout.InfiniteTimeSpan,
                },
            };
            HealthCheckResult result1 = await healthCheck.CheckHealthAsync(context, default);
            HealthCheckResult result2 = await healthCheck.CheckHealthAsync(context, default);

            // Assert
            VerifyGetContainersCalled(Times.Once());
            result1.Status.Should().Be(HealthStatus.Healthy);
            result1.Exception.Should().BeNull();
            result2.Status.Should().Be(HealthStatus.Healthy);
            result2.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_GetClientFails_Unhealthy()
        {
            // Arrange
            InvalidOperationException ex = new("Failed to create BlobServiceClient.");
            BlobServiceClient client = null;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Throws(ex);

            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            // Assert
            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().Be(ex);
            result.Data.Should().Contain("Area", "configuration");
            result.Data.Should().Contain("ConfigurationSection", "AzureWebJobsStorage");
        }

        [Fact]
        public async Task CheckHealthAsync_WithException_Unhealthy()
        {
            // Arrange
            RequestFailedException rfe = new(
                401, "Some exception message", "SomeErrorCode", null);
            SetupGetContainers(rfe);
            BlobServiceClient client = _mockBlobServiceClient.Object;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Returns(true);

            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            // Assert
            VerifyGetContainersCalled(Times.Once());
            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().Be(rfe);
            result.Description.Should().Be("Unable to access AzureWebJobsStorage");
            result.Data.Should().Contain("Area", "connectivity");
            result.Data.Should().Contain("ConfigurationSection", "AzureWebJobsStorage");
            result.Data.Should().Contain("StatusCode", 401);
            result.Data.Should().Contain("ErrorCode", "SomeErrorCode");
        }

        private void SetupGetContainers(AsyncPageable<BlobContainerItem> pageable)
        {
            _mockBlobServiceClient.Setup(c => c.GetBlobContainersAsync(
                BlobContainerTraits.None, BlobContainerStates.None, null, It.IsAny<CancellationToken>()))
                .Returns(pageable);
        }

        private void SetupGetContainers(RequestFailedException ex)
        {
            _mockBlobServiceClient.Setup(c => c.GetBlobContainersAsync(
                BlobContainerTraits.None, BlobContainerStates.None, null, It.IsAny<CancellationToken>()))
                .Throws(ex);
        }

        private void VerifyGetContainersCalled(Times times)
        {
            _mockBlobServiceClient.Verify(c => c.GetBlobContainersAsync(
                BlobContainerTraits.None, BlobContainerStates.None, null, It.IsAny<CancellationToken>()), times);
        }
    }
}
