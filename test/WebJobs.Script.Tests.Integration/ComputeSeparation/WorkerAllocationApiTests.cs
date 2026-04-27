// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// Integration tests for the worker linking admin APIs.
/// Uses TestFunctionHost with a mocked <see cref="IWorkerConnectionManager"/>
/// to verify the full HTTP pipeline: routing, auth, serialization.
/// </summary>
public class WorkerAllocationApiTests : IAsyncLifetime
{
    private readonly Mock<IWorkerConnectionManager> _mockConnectionManager;
    private readonly string _tempDir;
    private TestFunctionHost _host;

    public WorkerAllocationApiTests()
    {
        _mockConnectionManager = new Mock<IWorkerConnectionManager>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"WorkerAllocationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public Task InitializeAsync()
    {
        _host = new TestFunctionHost(
            _tempDir,
            Path.Combine(_tempDir, "logs"),
            configureWebHostServices: services =>
            {
                services.AddSingleton(_mockConnectionManager.Object);
            });

        // TestFunctionHost.StartAsync() already polls IsHostStarted() before
        // the constructor returns, so the host is ready at this point.
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            // Gracefully stop the host before deleting the temp directory. This stops
            // hosted services (including FileMonitoringService) so file watchers won't
            // fire after the directory is deleted, avoiding DirectoryNotFoundException.
            await _host.WebHost.StopAsync();
            _host.Dispose();
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task LinkWorker_Returns200()
    {
        _mockConnectionManager
            .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new
        {
            WorkerPodName = "w_test1234",
            WorkerHttpEndpoint = "http://10.0.1.42:48830",
            WorkerGrpcEndpoint = "http://10.0.1.42:50051",
            WorkerContainerEncryptionKey = "test-key"
        };
        var response = await SendAdminRequest(HttpMethod.Put, "admin/workers/w_test1234", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LinkWorker_MissingEndpoint_Returns400()
    {
        var request = new { WorkerPodName = "w_test1234" };
        var response = await SendAdminRequest(HttpMethod.Put, "admin/workers/w_test1234", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithoutAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "admin/workers/w_test1234");
        request.Content = ComputeSeparationTestHelpers.CreateJsonContent(new { WorkerGrpcEndpoint = "http://10.0.1.42:50051" });
        var response = await _host.HttpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stop_Returns202()
    {
        var response = await SendAdminRequest(HttpMethod.Post, "admin/host/stop");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Stop_WithMockedWorkers_CallsDisconnect()
    {
        var drainCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Link two workers first
        _mockConnectionManager
            .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockConnectionManager
            .Setup(m => m.GetWorkerStatus(It.IsAny<string>()))
            .Returns((WorkerConnectionInfo)null);
        _mockConnectionManager
            .Setup(m => m.DrainAndDisconnectAllAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                drainCalled.TrySetResult();
                return Task.CompletedTask;
            });

        await SendAdminRequest(HttpMethod.Put, "admin/workers/w1",
            new
            {
                WorkerPodName = "w1",
                WorkerGrpcEndpoint = "http://10.0.1.1:50051"
            });
        await SendAdminRequest(HttpMethod.Put, "admin/workers/w2",
            new
            {
                WorkerPodName = "w2",
                WorkerGrpcEndpoint = "http://10.0.1.2:50051"
            });

        var response = await SendAdminRequest(HttpMethod.Post, "admin/host/stop");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Wait for the fire-and-forget to invoke DrainAndDisconnectAllAsync.
        await drainCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _mockConnectionManager.Verify(
            m => m.DrainAndDisconnectAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task<HttpResponseMessage> SendAdminRequest(HttpMethod method, string path, object body = null)
    {
        string masterKey = await _host.GetMasterKeyAsync();
        var request = new HttpRequestMessage(method, $"{path}?code={masterKey}");

        if (body is not null)
        {
            request.Content = ComputeSeparationTestHelpers.CreateJsonContent(body);
        }

        return await _host.HttpClient.SendAsync(request);
    }
}
