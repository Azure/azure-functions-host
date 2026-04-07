// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// End-to-end integration test that verifies the config-driven compute-separation
/// pipeline: runtime starts with <c>FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT</c>
/// configured, auto-connects to the worker proxy on startup, and can invoke functions.
/// Uses TestFunctionHost (in-process) with WorkerProxy + MockWorker as child processes.
/// </summary>
[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, nameof(ExternalWorkerEndToEndTests))]
public class ExternalWorkerEndToEndTests : IAsyncLifetime, IDisposable
{
    // Use high port numbers to avoid conflicts with other tests or local services.
    private const int RuntimeGrpcPort = 60051;
    private const int WorkerGrpcPort = 60052;
    private const int HttpProxyPort = 60053;

    private readonly ITestOutputHelper _output;
    private readonly ConcurrentBag<string> _workerProxyLogs = new();
    private readonly ConcurrentBag<string> _mockWorkerLogs = new();

    private Process _workerProxyProcess;
    private Process _mockWorkerProcess;
    private TestFunctionHost _host;
    private string _scriptRootPath;

    public ExternalWorkerEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        string repoRoot = ComputeSeparationTestHelpers.FindRepoRoot();
        _output.WriteLine($"Repository root: {repoRoot}");

        _scriptRootPath = Path.Combine(Path.GetTempPath(), $"FunctionsE2E_{Guid.NewGuid():N}");
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

        // 3. Start the Functions runtime in-process via TestFunctionHost.
        //    Config-driven mode: GRPC_ENDPOINT is set so WorkerConnectionService
        //    auto-connects on startup.
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true");
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalGrpcEndpoint, $"http://localhost:{RuntimeGrpcPort}");
        Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node");

        _host = new TestFunctionHost(
            _scriptRootPath,
            Path.Combine(_scriptRootPath, "logs"));

        _output.WriteLine("TestFunctionHost created. Waiting for host to be ready...");

        await WaitForHostReadyAsync(TimeSpan.FromMinutes(2));
        _output.WriteLine("Host is ready.");
    }

    [Fact]
    public async Task HttpTrigger_ThroughWorkerProxy_ReturnsExpectedResponse()
    {
        HttpResponseMessage response = await _host.HttpClient.GetAsync("/api/HttpTrigger");

        string body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response: {response.StatusCode} — {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hello from mock worker!", body);
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
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalGrpcEndpoint, null);
        Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);

        GC.SuppressFinalize(this);
    }

    private async Task WaitForHostReadyAsync(TimeSpan timeout)
    {
        string masterKey = await _host.GetMasterKeyAsync();
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            ComputeSeparationTestHelpers.EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", _workerProxyLogs);
            ComputeSeparationTestHelpers.EnsureProcessRunning(_mockWorkerProcess, "MockWorker", _mockWorkerLogs);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"admin/host/status?code={masterKey}");
                using var response = await _host.HttpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    if (body.Contains("\"Running\"", StringComparison.OrdinalIgnoreCase))
                    {
                        _output.WriteLine($"Runtime is ready after {sw.Elapsed.TotalSeconds:F1}s.");
                        return;
                    }

                    _output.WriteLine($"Host status: {body} ({sw.Elapsed.TotalSeconds:F1}s)");
                }
            }
            catch
            {
                // Host not ready yet — will retry.
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException(
            $"Runtime did not become ready within {timeout.TotalSeconds}s.");
    }
}
