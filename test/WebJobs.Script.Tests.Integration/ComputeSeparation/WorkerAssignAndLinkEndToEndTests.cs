// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// End-to-end tests for the worker assign → runtime link flow.
/// Exercises the worker proxy's managed initialization with two ordering scenarios:
/// <list type="bullet">
/// <item><b>Assign-first</b>: <c>/admin/worker/assign</c> drives init + specialization + metadata prefetch,
/// then the runtime links and receives cached responses.</item>
/// <item><b>Link-first</b>: The runtime links and sends <c>WorkerInitRequest</c> first. The proxy blocks
/// until <c>/admin/worker/assign</c> completes, then replays cached responses.</item>
/// </list>
/// </summary>
[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, nameof(WorkerAssignAndLinkEndToEndTests))]
public class WorkerAssignAndLinkEndToEndTests : IAsyncLifetime, IDisposable
{
    private const int RuntimeGrpcPort = 60081;
    private const int WorkerGrpcPort = 60082;
    private const int HttpProxyPort = 60083;
    private const int ManagementPort = 60084;

    private readonly ITestOutputHelper _output;
    private readonly ConcurrentBag<string> _workerProxyLogs = new();
    private readonly ConcurrentBag<string> _mockWorkerLogs = new();

    private Process _workerProxyProcess;
    private Process _mockWorkerProcess;
    private TestFunctionHost _host;
    private string _scriptRootPath;

    public WorkerAssignAndLinkEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        string repoRoot = ComputeSeparationTestHelpers.FindRepoRoot();
        _output.WriteLine($"Repository root: {repoRoot}");

        _scriptRootPath = Path.Combine(Path.GetTempPath(), $"FunctionsAssignE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scriptRootPath);

        string workerProxyDll = ComputeSeparationTestHelpers.FindBuiltDll(repoRoot, "src", "Functions.WorkerProxy");
        string mockWorkerDll = ComputeSeparationTestHelpers.FindBuiltDll(repoRoot, "tools", "ComputeSeparation", "MockWorker");

        // Start the worker proxy.
        _workerProxyProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output, "dotnet",
            $"\"{workerProxyDll}\" --runtime-grpc-port {RuntimeGrpcPort} --worker-grpc-port {WorkerGrpcPort} --http-proxy-port {HttpProxyPort} --management-port {ManagementPort}",
            _workerProxyLogs, "WorkerProxy");

        await Task.Delay(2000);
        ComputeSeparationTestHelpers.EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", _workerProxyLogs);

        // Start the mock worker (connects to worker proxy gRPC).
        _mockWorkerProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output, "dotnet",
            $"\"{mockWorkerDll}\" --grpc-endpoint http://localhost:{WorkerGrpcPort}",
            _mockWorkerLogs, "MockWorker");

        await Task.Delay(3000);
        ComputeSeparationTestHelpers.EnsureProcessRunning(_mockWorkerProcess, "MockWorker", _mockWorkerLogs);

        // Start the runtime (external worker mode, API-driven).
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true");
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");

        _host = new TestFunctionHost(
            _scriptRootPath,
            Path.Combine(_scriptRootPath, "logs"),
            skipHostStartupWait: true);

        _output.WriteLine("All processes started.");
    }

    /// <summary>
    /// Assign-first: <c>/assign</c> completes before <c>/link</c>.
    /// The proxy drives the full init sequence with the worker, caches responses,
    /// then the runtime links and receives pre-baked answers immediately.
    /// </summary>
    [Fact]
    public async Task AssignFirst_ThenLink_FullFlow()
    {
        using var proxyClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{ManagementPort}") };

        // 1. Assign first — drives init + specialize + metadata prefetch.
        _output.WriteLine("Step 1: Calling /admin/worker/assign on worker proxy.");
        var assignResponse = await CallWorkerAssignAsync(proxyClient);
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        // 2. Link the runtime to the worker proxy.
        _output.WriteLine("Step 2: Calling PUT /admin/workers/{workerId} on runtime.");
        string masterKey = await _host.GetMasterKeyAsync();
        var linkResponse = await CallWorkerLinkAsync(masterKey);
        Assert.Equal(HttpStatusCode.Accepted, linkResponse.StatusCode);

        // 3. Wait for host to be running and invoke a function.
        await WaitForHostReadyAsync(masterKey, TimeSpan.FromMinutes(2));
        await AssertHttpTriggerInvocationAsync();
    }

    /// <summary>
    /// Link-first: The runtime links and sends <c>WorkerInitRequest</c> before
    /// <c>/assign</c> is called. The proxy blocks the runtime's request until
    /// specialization completes, then replays cached responses.
    /// </summary>
    [Fact]
    public async Task LinkFirst_ThenAssign_FullFlow()
    {
        using var proxyClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{ManagementPort}") };
        string masterKey = await _host.GetMasterKeyAsync();

        // 1. Link first — runtime connects and sends WorkerInitRequest.
        //    The proxy blocks because specialization hasn't completed yet.
        _output.WriteLine("Step 1: Calling PUT /admin/workers/{workerId} on runtime (before assign).");
        var linkResponse = await CallWorkerLinkAsync(masterKey);
        Assert.Equal(HttpStatusCode.Accepted, linkResponse.StatusCode);

        // Give the runtime a moment to connect and send WorkerInitRequest to the proxy.
        await Task.Delay(2000);

        // 2. Now assign — proxy drives init + specialize + metadata prefetch,
        //    then unblocks the runtime's pending WorkerInitRequest.
        _output.WriteLine("Step 2: Calling /admin/worker/assign on worker proxy.");
        var assignResponse = await CallWorkerAssignAsync(proxyClient);
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        // 3. Wait for host to be running and invoke a function.
        await WaitForHostReadyAsync(masterKey, TimeSpan.FromMinutes(2));
        await AssertHttpTriggerInvocationAsync();
    }

    // --- Shared helpers ---

    private static async Task<HttpResponseMessage> CallWorkerAssignAsync(HttpClient proxyClient)
    {
        var assignPayload = new
        {
            environment = new { FUNCTIONS_WORKER_RUNTIME = "node" },
            functionAppDirectory = "/home/site/wwwroot"
        };

        return await proxyClient.PostAsync("/admin/worker/assign",
            new StringContent(JsonConvert.SerializeObject(assignPayload), Encoding.UTF8, "application/json"));
    }

    private async Task<HttpResponseMessage> CallWorkerLinkAsync(string masterKey)
    {
        var linkRequest = new
        {
            workerId = "w_assign_e2e",
            podName = "worker-pod-assign-e2e",
            grpcEndpoint = $"http://localhost:{RuntimeGrpcPort}",
            podKey = "test-key"
        };

        return await SendAdminRequest(masterKey, HttpMethod.Put, $"admin/workers/{linkRequest.workerId}", linkRequest);
    }

    private async Task AssertHttpTriggerInvocationAsync()
    {
        _output.WriteLine("Step 3: Invoking /api/HttpTrigger.");
        var invokeResponse = await _host.HttpClient.GetAsync("/api/HttpTrigger");
        string invokeBody = await invokeResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Invoke response: {invokeResponse.StatusCode} — {invokeBody}");

        Assert.Equal(HttpStatusCode.OK, invokeResponse.StatusCode);
        Assert.Contains("Hello from mock worker!", invokeBody);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _host?.Dispose();
        ComputeSeparationTestHelpers.KillProcess(_output, _mockWorkerProcess, "MockWorker");
        ComputeSeparationTestHelpers.KillProcess(_output, _workerProxyProcess, "WorkerProxy");
        ComputeSeparationTestHelpers.TryDeleteDirectory(_scriptRootPath);

        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalEnabled, null);
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, null);

        GC.SuppressFinalize(this);
    }

    private async Task<HttpResponseMessage> SendAdminRequest(
        string masterKey, HttpMethod method, string path, object body = null)
    {
        var request = new HttpRequestMessage(method, $"{path}?code={masterKey}");

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        }

        return await _host.HttpClient.SendAsync(request);
    }

    private async Task WaitForHostReadyAsync(string masterKey, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            try
            {
                var response = await SendAdminRequest(masterKey, HttpMethod.Get, "admin/host/status");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var status = JObject.Parse(await response.Content.ReadAsStringAsync());
                    string state = status["state"]?.ToString();
                    _output.WriteLine($"Host state: {state} ({sw.Elapsed.TotalSeconds:F1}s)");

                    if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Host not ready yet.
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Host did not reach Running state within {timeout.TotalSeconds}s.");
    }
}
