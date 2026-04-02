// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// Integration tests for the worker allocation admin APIs.
/// Uses TestFunctionHost with a mocked <see cref="IWorkerConnectionManager"/>
/// to verify the full HTTP pipeline: routing, auth, serialization.
/// </summary>
public class WorkerAllocationApiTests : IAsyncLifetime, IDisposable
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

    public async Task InitializeAsync()
    {
        _host = new TestFunctionHost(
            _tempDir,
            Path.Combine(_tempDir, "logs"),
            configureWebHostServices: services =>
            {
                services.AddSingleton(_mockConnectionManager.Object);
            });

        // TestFunctionHost starts automatically in its constructor.
        // Wait for the host to be ready.
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _host?.Dispose();

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
    public async Task AssignWorker_Returns202_WithWorkerConnectionInfo()
    {
        _mockConnectionManager
            .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockConnectionManager
            .Setup(m => m.GetWorkerStatus(It.IsAny<string>()))
            .Returns<string>(id => new WorkerConnectionInfo
            {
                WorkerId = id,
                State = WorkerConnectionState.Connecting
            });

        var request = new { workerId = "w_test1234", grpcEndpoint = "http://10.0.1.42:50051" };
        var response = await SendAdminRequest(HttpMethod.Post, "admin/workers/assign", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("w_test1234", body["workerId"]?.ToString());
        Assert.Equal("Connecting", body["state"]?.ToString());
    }

    [Fact]
    public async Task AssignWorker_MissingEndpoint_Returns400()
    {
        var request = new { workerId = "w_test1234" };
        var response = await SendAdminRequest(HttpMethod.Post, "admin/workers/assign", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAllWorkers_ReturnsWorkerList()
    {
        _mockConnectionManager
            .Setup(m => m.GetWorkerStatuses())
            .Returns(new List<WorkerConnectionInfo>
            {
                new() { WorkerId = "w_1", State = WorkerConnectionState.Connected },
                new() { WorkerId = "w_2", State = WorkerConnectionState.Connecting }
            }.AsReadOnly());

        var response = await SendAdminRequest(HttpMethod.Get, "admin/workers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JArray.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.Count);
        Assert.Equal("Connected", body[0]["state"]?.ToString());
    }

    [Fact]
    public async Task GetWorker_KnownId_ReturnsWorkerInfo()
    {
        _mockConnectionManager
            .Setup(m => m.GetWorkerStatus("w_1"))
            .Returns(new WorkerConnectionInfo { WorkerId = "w_1", State = WorkerConnectionState.Connected });

        var response = await SendAdminRequest(HttpMethod.Get, "admin/workers/w_1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("w_1", body["workerId"]?.ToString());
        Assert.Equal("Connected", body["state"]?.ToString());
    }

    [Fact]
    public async Task GetWorker_UnknownId_Returns404()
    {
        _mockConnectionManager
            .Setup(m => m.GetWorkerStatus("w_unknown"))
            .Returns((WorkerConnectionInfo)null);

        var response = await SendAdminRequest(HttpMethod.Get, "admin/workers/w_unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWorker_Returns200_WithDisconnectedState()
    {
        _mockConnectionManager
            .Setup(m => m.GetWorkerStatus("w_1"))
            .Returns(new WorkerConnectionInfo { WorkerId = "w_1", State = WorkerConnectionState.Connected });

        _mockConnectionManager
            .Setup(m => m.DisconnectWorkerAsync("w_1", It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _mockConnectionManager
                    .Setup(m => m.GetWorkerStatus("w_1"))
                    .Returns(new WorkerConnectionInfo { WorkerId = "w_1", State = WorkerConnectionState.Disconnected });
            })
            .Returns(Task.CompletedTask);

        var response = await SendAdminRequest(HttpMethod.Delete, "admin/workers/w_1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Disconnected", body["state"]?.ToString());
    }

    [Fact]
    public async Task DeleteWorker_UnknownId_Returns404()
    {
        _mockConnectionManager
            .Setup(m => m.GetWorkerStatus("w_unknown"))
            .Returns((WorkerConnectionInfo)null);

        var response = await SendAdminRequest(HttpMethod.Delete, "admin/workers/w_unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithoutAuth_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "admin/workers");
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
