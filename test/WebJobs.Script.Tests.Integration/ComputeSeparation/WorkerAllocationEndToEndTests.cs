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
/// End-to-end integration test that verifies the API-driven worker linking flow.
/// Unlike <see cref="ExternalWorkerEndToEndTests"/> which auto-connects via an environment
/// variable, this test starts the runtime with no pre-configured worker endpoint and uses
/// the <c>PUT /admin/workers/{workerId}</c> API to link a worker at runtime.
///
/// Flow:
/// 1. Worker proxy + mock worker start as child processes
/// 2. Runtime starts via TestFunctionHost (external worker enabled, no gRPC endpoint)
/// 3. <c>PUT /admin/workers/{workerId}</c> connects the worker
/// 4. <c>GET /api/HttpTrigger</c> invokes a function through the mock worker
/// </summary>
[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, nameof(WorkerAllocationEndToEndTests))]
public class WorkerAllocationEndToEndTests : IAsyncLifetime, IDisposable
{
    // Use unique port numbers to avoid conflicts.
    private const int RuntimeGrpcPort = 60061;
    private const int WorkerGrpcPort = 60062;
    private const int HttpProxyPort = 60063;
    private const int ManagementPort = 60064;

    private readonly ITestOutputHelper _output;
    private readonly ConcurrentBag<string> _workerProxyLogs = new();
    private readonly ConcurrentBag<string> _mockWorkerLogs = new();

    private Process _workerProxyProcess;
    private Process _mockWorkerProcess;
    private TestFunctionHost _host;
    private string _scriptRootPath;

    public WorkerAllocationEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        string repoRoot = ComputeSeparationTestHelpers.FindRepoRoot();
        _output.WriteLine($"Repository root: {repoRoot}");

        _scriptRootPath = Path.Combine(Path.GetTempPath(), $"FunctionsM4E2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scriptRootPath);
        _output.WriteLine($"Script root: {_scriptRootPath}");

        string workerProxyDll = ComputeSeparationTestHelpers.FindBuiltDll(repoRoot, "src", "Functions.WorkerProxy");
        string mockWorkerDll = ComputeSeparationTestHelpers.FindBuiltDll(repoRoot, "tools", "ComputeSeparation", "MockWorker");

        _output.WriteLine($"Worker proxy DLL: {workerProxyDll}");
        _output.WriteLine($"MockWorker DLL: {mockWorkerDll}");

        // 1. Start the worker proxy relay.
        _workerProxyProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output,
            "dotnet",
            $"\"{workerProxyDll}\" --runtime-grpc-port {RuntimeGrpcPort} --worker-grpc-port {WorkerGrpcPort} --http-proxy-port {HttpProxyPort} --management-port {ManagementPort}",
            _workerProxyLogs,
            "WorkerProxy");

        await Task.Delay(2000);
        ComputeSeparationTestHelpers.EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", _workerProxyLogs);

        // 2. Start the mock worker.
        _mockWorkerProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output,
            "dotnet",
            $"\"{mockWorkerDll}\" --grpc-endpoint http://localhost:{WorkerGrpcPort}",
            _mockWorkerLogs,
            "MockWorker");

        await ComputeSeparationTestHelpers.WaitForWorkerProxyReadyAsync(ManagementPort, _output);
        ComputeSeparationTestHelpers.EnsureProcessRunning(_mockWorkerProcess, "MockWorker", _mockWorkerLogs);

        // 3. Start the runtime via TestFunctionHost.
        //    External worker mode is enabled but NO gRPC endpoint is configured (API-driven mode).
        //    WebJobsScriptHostService is NOT registered as a hosted service in external worker
        //    mode, so the TestFunctionHost constructor returns immediately without blocking.
        //    The ScriptHost starts when the first worker is assigned via the admin API.
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true");
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");

        _host = new TestFunctionHost(
            _scriptRootPath,
            Path.Combine(_scriptRootPath, "logs"),
            skipHostStartupWait: true);

        _output.WriteLine("TestFunctionHost is ready (waiting for worker assignment).");
    }

    [Fact]
    public async Task WorkerLinkFlow_LinkAndInvoke()
    {
        string masterKey = await _host.GetMasterKeyAsync();
        _output.WriteLine("Got master key for admin API calls.");

        // 1. Link a worker via the admin API.
        var linkRequest = new
        {
            workerId = "w_e2etest01",
            podName = "worker-pod-e2e",
            grpcEndpoint = $"http://localhost:{RuntimeGrpcPort}",
            podKey = "test-key"
        };

        var linkResponse = await SendAdminRequest(
            masterKey, HttpMethod.Put, $"admin/workers/{linkRequest.workerId}", linkRequest);

        string linkBody = await linkResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Link response: {linkResponse.StatusCode} — {linkBody}");
        Assert.Equal(HttpStatusCode.Accepted, linkResponse.StatusCode);

        // 2. Wait until the host is running (worker connected and ScriptHost started).
        await WaitForHostReadyAsync(masterKey, TimeSpan.FromMinutes(2));
        _output.WriteLine("Host is ready.");

        // 3. Invoke a function through the mock worker.
        var invokeResponse = await _host.HttpClient.GetAsync("/api/HttpTrigger");
        string invokeBody = await invokeResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Invoke response: {invokeResponse.StatusCode} — {invokeBody}");

        Assert.Equal(HttpStatusCode.OK, invokeResponse.StatusCode);
        Assert.Contains("Hello from mock worker!", invokeBody);
    }

    [Fact]
    public async Task WorkerLinkFlow_StopDrainsAndDisconnects()
    {
        string masterKey = await _host.GetMasterKeyAsync();

        // 1. Link a worker.
        var linkRequest = new
        {
            workerId = "w_e2estop01",
            podName = "worker-pod-stop",
            grpcEndpoint = $"http://localhost:{RuntimeGrpcPort}",
            podKey = "test-key"
        };

        var linkResponse = await SendAdminRequest(
            masterKey, HttpMethod.Put, $"admin/workers/{linkRequest.workerId}", linkRequest);
        Assert.Equal(HttpStatusCode.Accepted, linkResponse.StatusCode);

        // 2. Wait for host ready.
        await WaitForHostReadyAsync(masterKey, TimeSpan.FromMinutes(2));
        _output.WriteLine("Host is ready.");

        // 3. Verify worker proxy is in ReadyForRequest state.
        using var proxyClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{ManagementPort}") };
        var stateResponse = await proxyClient.PostAsync("/instanceState",
            new StringContent("{\"revisionId\": 0}", Encoding.UTF8, "application/json"));
        var state = JObject.Parse(await stateResponse.Content.ReadAsStringAsync());
        _output.WriteLine($"Worker proxy state before stop: {state}");
        Assert.Equal("ReadyForRequest", state["currentPodStatusTransition"]?["toPodStatus"]?.ToString());

        // 4. Call /admin/host/stop.
        var stopResponse = await SendAdminRequest(masterKey, HttpMethod.Post, "admin/host/stop");
        _output.WriteLine($"Stop response: {stopResponse.StatusCode}");
        Assert.Equal(HttpStatusCode.Accepted, stopResponse.StatusCode);

        // 5. Poll worker proxy /instanceState until it reaches MarkForDeletion.
        int lastRevision = state["revisionId"]?.Value<int>() ?? 0;
        var sw = Stopwatch.StartNew();
        string finalStatus = null;

        while (sw.Elapsed < TimeSpan.FromMinutes(2))
        {
            try
            {
                var pollResponse = await proxyClient.PostAsync("/instanceState",
                    new StringContent($"{{\"revisionId\": {lastRevision}}}", Encoding.UTF8, "application/json"));

                if (pollResponse.StatusCode == HttpStatusCode.OK)
                {
                    var pollState = JObject.Parse(await pollResponse.Content.ReadAsStringAsync());
                    finalStatus = pollState["currentPodStatusTransition"]?["toPodStatus"]?.ToString();
                    lastRevision = pollState["revisionId"]?.Value<int>() ?? lastRevision;
                    _output.WriteLine($"Worker proxy state: {finalStatus} (revision {lastRevision}, {sw.Elapsed.TotalSeconds:F1}s)");

                    if (string.Equals(finalStatus, "MarkForDeletion", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Proxy may have shut down — that's also an acceptable outcome.
                _output.WriteLine("Worker proxy connection lost — process may have exited.");
                break;
            }

            await Task.Delay(500);
        }

        Assert.Equal("MarkForDeletion", finalStatus);
        _output.WriteLine("Worker proxy reached MarkForDeletion — stop completed successfully.");
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
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json");
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
