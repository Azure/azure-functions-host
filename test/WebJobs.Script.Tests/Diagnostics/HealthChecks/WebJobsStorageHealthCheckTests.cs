// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Moq.Protected;
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
            _mockBlobServiceClient.Setup(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName))
                .Returns(_mockContainerClient.Object);
        }

        private interface IHandlerMethods
        {
            Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation);
        }

        [Fact]
        public void Constructor_WithNullProvider_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new WebJobsStorageHealthCheck(null, _environment, _scriptHostManager.Object))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("provider");
        }

        [Fact]
        public void Constructor_WithNullEnvironment_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new WebJobsStorageHealthCheck(_provider.Object, null, _scriptHostManager.Object))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("environment");
        }

        [Fact]
        public void Constructor_WithNullScriptHostManager_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new WebJobsStorageHealthCheck(_provider.Object, _environment, null))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("scriptHostManager");
        }

        [Fact]
        public async Task CheckHealthAsync_WithNullContext_ThrowsArgumentNullException()
        {
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            await TestHelpers.Act(async () => await healthCheck.CheckHealthAsync(null, default))
                .Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("context");
        }

        [Fact]
        public async Task CheckHealthAsync_InPlaceholderMode_ReturnsHealthyWithSkippedMessage()
        {
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            result.Status.Should().Be(HealthStatus.Healthy);
            result.Description.Should().Be("Placeholder mode. Check skipped.");
            result.Exception.Should().BeNull();
            VerifyContainerProbeCalled(Times.Never());
            _provider.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CheckHealthAsync_WithNoActiveScriptHost_ReturnsHealthyWithSkippedMessage()
        {
            _scriptHostManager.SetupGet(m => m.Services).Returns((IServiceProvider)null);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            result.Status.Should().Be(HealthStatus.Healthy);
            result.Description.Should().Be("No active script host. Check skipped.");
            result.Exception.Should().BeNull();
            VerifyContainerProbeCalled(Times.Never());
            _provider.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckHealthAsync_ChecksBlobConnectivity(bool exists)
        {
            SetupContainerProbe(exists);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext, default);

            VerifyHealthy(result, exists);
            VerifyContainerProbeCalled(Times.Once());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CheckHealthAsync_Twice_ReturnsCachedResult(bool exists)
        {
            SetupContainerProbe(exists);
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

            VerifyHealthy(first, exists);
            VerifyHealthy(second, exists);
            VerifyContainerProbeCalled(Times.Once());
        }

        [Fact]
        public async Task CheckHealthAsync_GetClientFails_Unhealthy()
        {
            InvalidOperationException exception = new("Failed to create BlobServiceClient.");
            BlobServiceClient client = null;
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Throws(exception);
            await using WebJobsStorageHealthCheck healthCheck = new(_provider.Object, _environment, _scriptHostManager.Object);

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext);

            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().BeSameAs(exception);
            result.Data.Should().Contain(nameof(HealthCheckData.Area), HealthCheckData.Areas.Configuration);
            result.Data.Should().Contain(nameof(HealthCheckData.ConfigurationSection), "AzureWebJobsStorage");
            VerifyContainerProbeCalled(Times.Never());
        }

        [Theory]
        [InlineData(401, "SomeErrorCode")]
        [InlineData(403, "AuthorizationPermissionMismatch")]
        [InlineData(403, "AuthenticationFailed")]
        [InlineData(403, "AuthorizationFailure")]
        [InlineData(403, "AuthorizationResourceTypeMismatch")]
        [InlineData(403, null)]
        [InlineData(404, "ResourceNotFound")]
        [InlineData(404, "BlobNotFound")]
        [InlineData(404, null)]
        [InlineData(503, "ServerBusy")]
        public async Task CheckHealthAsync_WithException_Unhealthy(int status, string errorCode)
        {
            RequestFailedException exception = new(status, "Container probe failed.", errorCode, null);
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext);

            VerifyConnectivityFailure(result, exception);
            VerifyContainerProbeCalled(Times.Once());
        }

        [Theory]
        [InlineData(200, null, HealthStatus.Healthy)]
        [InlineData(404, "ContainerNotFound", HealthStatus.Healthy)]
        [InlineData(404, "ResourceNotFound", HealthStatus.Unhealthy)]
        [InlineData(404, "BlobNotFound", HealthStatus.Unhealthy)]
        [InlineData(404, null, HealthStatus.Unhealthy)]
        [InlineData(403, "AuthorizationPermissionMismatch", HealthStatus.Unhealthy)]
        [InlineData(403, "AuthenticationFailed", HealthStatus.Unhealthy)]
        [InlineData(503, "ServerBusy", HealthStatus.Unhealthy)]
        public async Task CheckHealthAsync_WithStorageResponse_PreservesSdkErrorHandling(
            int status, string errorCode, HealthStatus expectedStatus)
        {
            using HttpResponseMessage response = new((HttpStatusCode)status)
            {
                Content = new StringContent(
                    $"<Error><Code>{errorCode}</Code><Message>Simulated storage response.</Message></Error>"),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
            response.Headers.ETag = new EntityTagHeaderValue("\"test\"");
            if (errorCode is not null)
            {
                response.Headers.Add("x-ms-error-code", errorCode);
            }

            Mock<HttpMessageHandler> handler = new();
            handler.Protected().As<IHandlerMethods>()
                .Setup(h => h.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            using HttpClient httpClient = new(handler.Object);
            BlobClientOptions options = new()
            {
                Transport = new HttpClientTransport(httpClient),
            };
            options.Retry.MaxRetries = 0;
            BlobServiceClient client = new(new Uri("https://storage.test.invalid"), options);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck(client);

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext);

            result.Status.Should().Be(expectedStatus);
            if (expectedStatus is HealthStatus.Healthy)
            {
                VerifyHealthy(result, status is 200);
            }
            else
            {
                RequestFailedException exception = result.Exception.Should().BeOfType<RequestFailedException>().Which;
                exception.Status.Should().Be(status);
                if (errorCode is not null)
                {
                    exception.ErrorCode.Should().Be(errorCode);
                }

                VerifyConnectivityFailure(result, exception);
            }

            handler.Protected().As<IHandlerMethods>().Verify(h => h.SendAsync(
                It.Is<HttpRequestMessage>(request => request.Method == HttpMethod.Get
                    && string.Equals(request.RequestUri.AbsoluteUri,
                        "https://storage.test.invalid/azure-webjobs-hosts?restype=container", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [Theory]
        [InlineData(403, "AuthorizationPermissionMismatch")]
        [InlineData(503, "ServerBusy")]
        public async Task CheckHealthCoreAsync_ContainerProbeFails_ReportsFailureAndRecovers(int status, string errorCode)
        {
            RequestFailedException exception = new(status, "Container access failed.", errorCode, null);
            _mockContainerClient.SetupSequence(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()))
                .ThrowsAsync(exception)
                .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult first = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult second = await healthCheck.CheckHealthCoreAsync(default);
            HealthCheckResult third = await healthCheck.CheckHealthCoreAsync(default);

            VerifyHealthy(first, true);
            VerifyConnectivityFailure(second, exception);
            VerifyHealthy(third, true);
            VerifyContainerProbeCalled(Times.Exactly(3));
            BlobServiceClient client = _mockBlobServiceClient.Object;
            _provider.Verify(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client), Times.Once);
            _provider.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CheckHealthCoreAsync_ContainerProbe_ForwardsCancellationTokenOnEveryRefresh()
        {
            SetupContainerProbe(true);
            using CancellationTokenSource firstCancellation = new();
            using CancellationTokenSource secondCancellation = new();
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            await healthCheck.CheckHealthCoreAsync(firstCancellation.Token);
            await healthCheck.CheckHealthCoreAsync(secondCancellation.Token);

            _mockContainerClient.Verify(c => c.ExistsAsync(firstCancellation.Token), Times.Once);
            _mockContainerClient.Verify(c => c.ExistsAsync(secondCancellation.Token), Times.Once);
            VerifyContainerProbeCalled(Times.Exactly(2));
        }

        [Fact]
        public async Task CheckHealthCoreAsync_ContainerProbeCanceled_Unhealthy()
        {
            using CancellationTokenSource cancellation = new();
            OperationCanceledException exception = new(cancellation.Token);
            _mockContainerClient.Setup(c => c.ExistsAsync(cancellation.Token)).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthCoreAsync(cancellation.Token);

            VerifyFailure(result, exception);
            result.Data.Should().Contain(nameof(HealthCheckData.ErrorCode), "OperationCanceled");
            VerifyContainerProbeCalled(Times.Once());
        }

        [Fact]
        public async Task CheckHealthAsync_ContainerProbeTimesOut_Unhealthy()
        {
            TimeoutException exception = new("Storage request timed out.");
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext);

            VerifyFailure(result, exception);
            result.Data.Should().Contain(nameof(HealthCheckData.ErrorCode), "Timeout");
            VerifyContainerProbeCalled(Times.Once());
        }

        [Fact]
        public async Task CheckHealthAsync_ContainerProbeNetworkFailure_Unhealthy()
        {
            HttpRequestException exception = new("Storage endpoint is unreachable.");
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(exception);
            await using WebJobsStorageHealthCheck healthCheck = CreateHealthCheck();

            HealthCheckResult result = await healthCheck.CheckHealthAsync(_healthCheckContext);

            VerifyFailure(result, exception);
            VerifyContainerProbeCalled(Times.Once());
        }

        private static void VerifyHealthy(HealthCheckResult result, bool exists)
        {
            result.Status.Should().Be(HealthStatus.Healthy);
            result.Exception.Should().BeNull();
            result.Description.Should().Be(exists ? null : "Storage is reachable. Host container does not exist.");
            result.Data.Should().BeEmpty();
        }

        private static void VerifyFailure(HealthCheckResult result, Exception exception)
        {
            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Exception.Should().BeSameAs(exception);
            result.Description.Should().Be("Unable to access AzureWebJobsStorage");
            result.Data.Should().Contain(nameof(HealthCheckData.Area), HealthCheckData.Areas.Connectivity);
            result.Data.Should().Contain(nameof(HealthCheckData.ConfigurationSection), "AzureWebJobsStorage");
        }

        private static void VerifyConnectivityFailure(HealthCheckResult result, RequestFailedException exception)
        {
            VerifyFailure(result, exception);
            result.Data.Should().Contain(nameof(HealthCheckData.StatusCode), exception.Status);
            result.Data.Should().Contain(nameof(HealthCheckData.ErrorCode), exception.ErrorCode);
        }

        private WebJobsStorageHealthCheck CreateHealthCheck()
        {
            return CreateHealthCheck(_mockBlobServiceClient.Object);
        }

        private WebJobsStorageHealthCheck CreateHealthCheck(BlobServiceClient client)
        {
            _provider.Setup(p => p.TryCreateBlobServiceClientFromConnection(
                ConnectionStringNames.Storage, out client)).Returns(true);

            return new WebJobsStorageHealthCheck(_provider.Object, _environment, _scriptHostManager.Object);
        }

        private void SetupContainerProbe(bool exists)
        {
            _mockContainerClient.Setup(c => c.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));
        }

        private void VerifyContainerProbeCalled(Times times)
        {
            _mockBlobServiceClient.Verify(c => c.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName), times);
            _mockContainerClient.Verify(c => c.ExistsAsync(It.IsAny<CancellationToken>()), times);
            _mockBlobServiceClient.VerifyNoOtherCalls();
            _mockContainerClient.VerifyNoOtherCalls();
        }
    }
}
