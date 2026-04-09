// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
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
    public async Task LinkWorker_Returns202_WithWorkerConnectionInfo()
    {
        _mockConnectionManager
            .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new { workerId = "w_test1234", podName = "worker-pod-abc123", grpcEndpoint = "http://10.0.1.42:50051", podKey = "test-key" };
        var response = await SendAdminRequest(HttpMethod.Post, "admin/workers/link", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("w_test1234", body["workerId"]?.ToString());
        Assert.Equal("Connecting", body["state"]?.ToString());
    }

    [Fact]
    public async Task LinkWorker_MissingEndpoint_Returns400()
    {
        var request = new { workerId = "w_test1234" };
        var response = await SendAdminRequest(HttpMethod.Post, "admin/workers/link", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithoutAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "admin/workers/link");
        request.Content = new StringContent(
            JsonConvert.SerializeObject(new { grpcEndpoint = "http://10.0.1.42:50051" }),
            Encoding.UTF8,
            "application/json");
        var response = await _host.HttpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAdminRequest(HttpMethod method, string path, object body = null)
    {
        string masterKey = await _host.GetMasterKeyAsync();
        var request = new HttpRequestMessage(method, $"{path}?code={masterKey}");

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json");
        }

        return await _host.HttpClient.SendAsync(request);
    }
}
