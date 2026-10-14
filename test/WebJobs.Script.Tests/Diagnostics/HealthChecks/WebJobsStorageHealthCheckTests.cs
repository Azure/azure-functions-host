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
        private readonly Mock<IAzureBlobStorageProvider> _provider = new(MockBehavior.Strict);
        private readonly TestEnvironment _environment = new();
        private readonly Mock<BlobServiceClient> _mockBlobServiceClient = new(MockBehavior.Strict);
        private readonly Mock<BlobContainerClient> _mockContainerClient = new(MockBehavior.Strict);
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
            VerifyGetPropertiesCalled(Times.Never());
            result.Status.Should().Be(HealthStatus.Healthy);
            result.Description.Should().Be("No active script host. Check skipped.");
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_ChecksBlobConnectivity()
        {
            // Arrange
            SetupGetProperties();
            BlobServiceClient client = _mockBlobServiceClient.Object;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Returns(true);

            WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            // Act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            // Assert
            VerifyGetPropertiesCalled(Times.Once());
            result.Status.Should().Be(HealthStatus.Healthy);
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_Twice_ReturnsCachedResult()
        {
            // Arrange
            SetupGetProperties();
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
            VerifyGetPropertiesCalled(Times.Once());
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

        [Theory]
        [InlineData(401, "SomeErrorCode")]
        [InlineData(401, "AuthorizationPermissionMismatch")]
        [InlineData(403, "AuthenticationFailed")]
        [InlineData(403, "AuthorizationFailure")]
        [InlineData(403, "AuthorizationResourceTypeMismatch")]
        [InlineData(403, "authorizationpermissionmismatch")]
        [InlineData(403, null)]
        [InlineData(404, "ResourceNotFound")]
        [InlineData(503, "ServerBusy")]
        public async Task CheckHealthAsync_WithException_Unhealthy(int status, string errorCode)
        {
            RequestFailedException rfe = new(
                status, "Some exception message", errorCode, null);
            SetupGetProperties(rfe);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            VerifyGetPropertiesCalled(Times.Once());
            _mockBlobServiceClient.VerifyNoOtherCalls();
            VerifyConnectivityFailure(result, rfe);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckHealthAsync_WithAuthorizationPermissionMismatch_FallsBackToContainer(bool exists)
        {
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(exists);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            result.Status.Should().Be(HealthStatus.Healthy);
            result.Exception.Should().BeNull();
            result.Description.Should().Be(exists ? null : "Storage is reachable. Host container does not exist.");
            result.Data.Should().BeEmpty();
            VerifyGetPropertiesCalled(Times.Once());
            VerifyContainerProbeCalled(Times.Once());
            _mockBlobServiceClient.Verify(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName), Times.Once);
            _mockBlobServiceClient.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CheckHealthAsync_WithFallback_ReturnsCachedResult()
        {
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(true);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();
            HealthCheckContext context = new()
            {
                Registration = new HealthCheckRegistration("test", healthCheck, null, null)
                {
                    Period = Timeout.InfiniteTimeSpan,
                },
            };

            HealthCheckResult first = await healthCheck.CheckHealthAsync(context);
            HealthCheckResult second = await healthCheck.CheckHealthAsync(context);

            first.Status.Should().Be(HealthStatus.Healthy);
            second.Status.Should().Be(HealthStatus.Healthy);
            VerifyGetPropertiesCalled(Times.Once());
            VerifyContainerProbeCalled(Times.Once());
        }

        [Fact]
        public async Task CheckHealthCoreAsync_ServiceProbeSucceeds_DoesNotAccessContainer()
        {
            SetupGetProperties();
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult first = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult second = await healthCheck.CheckHealthCoreAsync(default);

            first.Status.Should().Be(HealthStatus.Healthy);
            second.Status.Should().Be(HealthStatus.Healthy);
            VerifyGetPropertiesCalled(Times.Exactly(2));
            _mockBlobServiceClient.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckHealthCoreAsync_SuccessfulFallback_ReusesContainerProbe(bool exists)
        {
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(exists);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult first = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult second = await healthCheck.CheckHealthCoreAsync(default);

            first.Status.Should().Be(HealthStatus.Healthy);
            second.Status.Should().Be(HealthStatus.Healthy);
            second.Description.Should().Be(exists ? null : "Storage is reachable. Host container does not exist.");
            VerifyGetPropertiesCalled(Times.Once());
            VerifyContainerProbeCalled(Times.Exactly(2));
            _mockBlobServiceClient.Verify(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName), Times.Once);
            _mockBlobServiceClient.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(403, "AuthorizationPermissionMismatch")]
        [InlineData(403, "AuthenticationFailed")]
        [InlineData(404, "ResourceNotFound")]
        [InlineData(503, "ServerBusy")]
        public async Task CheckHealthAsync_ContainerProbeFails_Unhealthy(int status, string errorCode)
        {
            RequestFailedException exception = new(status, "Container probe failed.", errorCode, null);
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(true);
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext);

            VerifyConnectivityFailure(result, exception);
            VerifyGetPropertiesCalled(Times.Once());
            VerifyContainerProbeCalled(Times.Once());
        }

        [Fact]
        public async Task CheckHealthCoreAsync_FailedFallback_DoesNotSelectContainerProbe()
        {
            RequestFailedException exception = new(503, "Container probe failed.", "ServerBusy", null);
            _mockBlobServiceClient.SetupSequence(c => c.GetPropertiesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(CreatePermissionMismatch())
                .ReturnsAsync((Response<BlobServiceProperties>)null);
            SetupContainerProbe(true);
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult first = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult second = await healthCheck.CheckHealthCoreAsync(default);

            VerifyConnectivityFailure(first, exception);
            second.Status.Should().Be(HealthStatus.Healthy);
            second.Exception.Should().BeNull();
            VerifyGetPropertiesCalled(Times.Exactly(2));
            VerifyContainerProbeCalled(Times.Once());
        }

        [Fact]
        public async Task CheckHealthCoreAsync_FailedFallback_RetriesUntilSuccessful()
        {
            RequestFailedException exception = new(503, "Container probe failed.", "ServerBusy", null);
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(true);
            _mockContainerClient.SetupSequence(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception)
                .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()))
                .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult first = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult second = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult third = await healthCheck.CheckHealthCoreAsync(default);

            VerifyConnectivityFailure(first, exception);
            second.Status.Should().Be(HealthStatus.Healthy);
            third.Status.Should().Be(HealthStatus.Healthy);
            VerifyGetPropertiesCalled(Times.Exactly(2));
            VerifyContainerProbeCalled(Times.Exactly(3));
            _mockBlobServiceClient.Verify(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName), Times.Exactly(2));
        }

        [Fact]
        public async Task CheckHealthCoreAsync_SelectedContainerProbeFails_ReportsFailureAndRecovers()
        {
            RequestFailedException exception = new(403, "Container access was revoked.", "AuthorizationPermissionMismatch", null);
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(true);
            _mockContainerClient.SetupSequence(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()))
                .ThrowsAsync(exception)
                .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult first = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult second = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult third = await healthCheck.CheckHealthCoreAsync(default);

            first.Status.Should().Be(HealthStatus.Healthy);
            VerifyConnectivityFailure(second, exception);
            third.Status.Should().Be(HealthStatus.Healthy);
            VerifyGetPropertiesCalled(Times.Once());
            VerifyContainerProbeCalled(Times.Exactly(3));
            _mockBlobServiceClient.Verify(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName), Times.Once);
        }

        [Fact]
        public async Task CheckHealthCoreAsync_ContainerProbe_ForwardsCancellationTokenOnEveryRefresh()
        {
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(true);
            using CancellationTokenSource firstCancellation = new();
            using CancellationTokenSource secondCancellation = new();
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            await healthCheck.CheckHealthCoreAsync(firstCancellation.Token);
            await healthCheck.CheckHealthCoreAsync(secondCancellation.Token);

            _mockBlobServiceClient.Verify(c => c.GetPropertiesAsync(firstCancellation.Token), Times.Once);
            _mockContainerClient.Verify(c => c.ExistsAsync(firstCancellation.Token), Times.Once);
            _mockContainerClient.Verify(c => c.ExistsAsync(secondCancellation.Token), Times.Once);
        }

        [Fact]
        public async Task CheckHealthCoreAsync_ContainerProbeCanceled_Unhealthy()
        {
            SetupGetProperties(CreatePermissionMismatch());
            SetupContainerProbe(true);
            using CancellationTokenSource cancellation = new();
            OperationCanceledException exception = new(cancellation.Token);
            _mockContainerClient.Setup(c => c.ExistsAsync(cancellation.Token)).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthCoreAsync(cancellation.Token);

            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().BeSameAs(exception);
            result.Data.Should().Contain(nameof(HealthCheckData.Area), HealthCheckData.Areas.Connectivity);
            result.Data.Should().Contain(nameof(HealthCheckData.ErrorCode), "OperationCanceled");
        }

        private static RequestFailedException CreatePermissionMismatch()
        {
            return new RequestFailedException(
                403, "Service properties are not authorized.", BlobErrorCode.AuthorizationPermissionMismatch.ToString(), null);
        }

        private static void VerifyConnectivityFailure(HealthCheckResult result, RequestFailedException exception)
        {
            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().BeSameAs(exception);
            result.Description.Should().Be("Unable to access AzureWebJobsStorage");
            result.Data.Should().Contain(nameof(HealthCheckData.Area), HealthCheckData.Areas.Connectivity);
            result.Data.Should().Contain(nameof(HealthCheckData.ConfigurationSection), "AzureWebJobsStorage");
            result.Data.Should().Contain(nameof(HealthCheckData.StatusCode), exception.Status);
            result.Data.Should().Contain(nameof(HealthCheckData.ErrorCode), exception.ErrorCode);
        }

        private WebJobsStorageHealthCheck CreateHealthCheck()
        {
            BlobServiceClient client = _mockBlobServiceClient.Object;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Returns(true);

            return new WebJobsStorageHealthCheck(_provider.Object, _environment, _scriptHostManager.Object);
        }

        private void SetupContainerProbe(bool exists)
        {
            _mockBlobServiceClient.Setup(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName))
                .Returns(_mockContainerClient.Object);
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));
        }

        private void VerifyContainerProbeCalled(Times times)
        {
            _mockContainerClient.Verify(c => c.ExistsAsync(It.IsAny<CancellationToken>()), times);
        }

        private void SetupGetProperties()
        {
            _mockBlobServiceClient.Setup(c => c.GetPropertiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((Response<BlobServiceProperties>)null);
        }

        private void SetupGetProperties(RequestFailedException ex)
        {
            _mockBlobServiceClient.Setup(c => c.GetPropertiesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);
        }

        private void VerifyGetPropertiesCalled(Times times)
        {
            _mockBlobServiceClient.Verify(c => c.GetPropertiesAsync(It.IsAny<CancellationToken>()), times);
        }
    }
}
