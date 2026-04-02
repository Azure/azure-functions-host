// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
/// End-to-end integration test that verifies the API-driven worker allocation flow.
/// Unlike <see cref="ExternalWorkerEndToEndTests"/> which auto-connects via an environment
/// variable, this test starts the runtime with no pre-configured worker endpoint and uses
/// the <c>POST /admin/workers/assign</c> API to assign a worker at runtime.
///
/// Flow:
/// 1. Worker proxy + mock worker start as child processes
/// 2. Runtime starts via TestFunctionHost (external worker enabled, no gRPC endpoint)
/// 3. <c>POST /admin/workers/assign</c> connects the worker
/// 4. <c>GET /api/HttpTrigger</c> invokes a function through the mock worker
/// 5. <c>DELETE /admin/workers/{workerId}</c> deallocates the worker
/// </summary>
[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, nameof(WorkerAllocationEndToEndTests))]
public class WorkerAllocationEndToEndTests : IAsyncLifetime, IDisposable
{
    // Use unique port numbers to avoid conflicts.
    private const int RuntimeGrpcPort = 60061;
    private const int WorkerGrpcPort = 60062;
    private const int HttpProxyPort = 60063;

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
            $"\"{workerProxyDll}\" --runtime-grpc-port {RuntimeGrpcPort} --worker-grpc-port {WorkerGrpcPort} --http-proxy-port {HttpProxyPort}",
            _workerProxyLogs,
            "WorkerProxy");

        await Task.Delay(3000);
        ComputeSeparationTestHelpers.EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", _workerProxyLogs);

        // 2. Start the mock worker.
        _mockWorkerProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output,
            "dotnet",
            $"\"{mockWorkerDll}\" --grpc-endpoint http://localhost:{WorkerGrpcPort}",
            _mockWorkerLogs,
            "MockWorker");

        await Task.Delay(3000);
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
    public async Task WorkerAllocationFlow_AssignInvokeAndDeallocate()
    {
        string masterKey = await _host.GetMasterKeyAsync();
        _output.WriteLine("Got master key for admin API calls.");

        // 1. Assign a worker via the admin API.
        var assignRequest = new
        {
            workerId = "w_e2etest01",
            grpcEndpoint = $"http://localhost:{RuntimeGrpcPort}"
        };

        var assignResponse = await SendAdminRequest(
            masterKey, HttpMethod.Post, "admin/workers/assign", assignRequest);

        string assignBody = await assignResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Assign response: {assignResponse.StatusCode} — {assignBody}");
        Assert.Equal(HttpStatusCode.Accepted, assignResponse.StatusCode);

        // 2. Poll until the worker is connected and ScriptHost is running.
        await WaitForWorkerStateAsync(masterKey, "w_e2etest01", "Connected", TimeSpan.FromMinutes(2));
        _output.WriteLine("Worker is connected.");

        await WaitForHostReadyAsync(masterKey, TimeSpan.FromMinutes(1));
        _output.WriteLine("Host is ready.");

        // 3. Invoke a function through the mock worker.
        var invokeResponse = await _host.HttpClient.GetAsync("/api/HttpTrigger");
        string invokeBody = await invokeResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Invoke response: {invokeResponse.StatusCode} — {invokeBody}");

        Assert.Equal(HttpStatusCode.OK, invokeResponse.StatusCode);
        Assert.Contains("Hello from mock worker!", invokeBody);

        // 4. Verify worker status via GET /admin/workers and GET /admin/workers/{workerId}.
        var allWorkersResponse = await SendAdminRequest(masterKey, HttpMethod.Get, "admin/workers");
        Assert.Equal(HttpStatusCode.OK, allWorkersResponse.StatusCode);
        var allWorkers = JArray.Parse(await allWorkersResponse.Content.ReadAsStringAsync());
        _output.WriteLine($"GET /admin/workers: {allWorkers}");
        Assert.Single(allWorkers);
        Assert.Equal("w_e2etest01", allWorkers[0]["workerId"]?.ToString());
        Assert.Equal("Connected", allWorkers[0]["state"]?.ToString());

        var singleWorkerResponse = await SendAdminRequest(masterKey, HttpMethod.Get, "admin/workers/w_e2etest01");
        Assert.Equal(HttpStatusCode.OK, singleWorkerResponse.StatusCode);
        var singleWorker = JObject.Parse(await singleWorkerResponse.Content.ReadAsStringAsync());
        Assert.Equal("w_e2etest01", singleWorker["workerId"]?.ToString());
        Assert.Equal("Connected", singleWorker["state"]?.ToString());
        Assert.Null(singleWorker["errorMessage"]?.Value<string>());

        // 5. Deallocate the worker.
        var deleteResponse = await SendAdminRequest(
            masterKey, HttpMethod.Delete, "admin/workers/w_e2etest01");

        string deleteBody = await deleteResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Delete response: {deleteResponse.StatusCode} — {deleteBody}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var deleteResult = JObject.Parse(deleteBody);
        Assert.Equal("Disconnected", deleteResult["state"]?.ToString());

        // 6. Verify the worker is no longer connected.
        var statusResponse = await SendAdminRequest(masterKey, HttpMethod.Get, "admin/workers");
        var workers = JArray.Parse(await statusResponse.Content.ReadAsStringAsync());
        var worker = workers.FirstOrDefault(w => string.Equals(w["workerId"]?.ToString(), "w_e2etest01", StringComparison.Ordinal));
        Assert.NotNull(worker);
        Assert.NotEqual("Connected", worker["state"]?.ToString());
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

    private async Task WaitForWorkerStateAsync(
        string masterKey, string workerId, string expectedState, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            try
            {
                var response = await SendAdminRequest(masterKey, HttpMethod.Get, $"admin/workers/{workerId}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var info = JObject.Parse(await response.Content.ReadAsStringAsync());
                    string state = info["state"]?.ToString();
                    _output.WriteLine($"Worker '{workerId}' state: {state} ({sw.Elapsed.TotalSeconds:F1}s)");

                    if (string.Equals(state, expectedState, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Worker '{workerId}' entered Error state: {info["errorMessage"]}");
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Host not ready yet.
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException(
            $"Worker '{workerId}' did not reach '{expectedState}' state within {timeout.TotalSeconds}s.");
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
