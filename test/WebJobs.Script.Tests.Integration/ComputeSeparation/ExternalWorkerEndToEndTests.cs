// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// End-to-end integration tests that verify the full compute-separation pipeline:
/// Worker proxy ↔ MockWorker ↔ Runtime. All three components are launched as child
/// processes and an HTTP request is made to the runtime to verify the response
/// flows through the worker proxy relay and back.
/// </summary>
[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, nameof(ExternalWorkerEndToEndTests))]
public class ExternalWorkerEndToEndTests : IAsyncLifetime, IDisposable
{
    // Use high port numbers to avoid conflicts with other tests or local services.
    private const int RuntimeGrpcPort = 60051;
    private const int WorkerGrpcPort = 60052;
    private const int HttpProxyPort = 60053;
    private const int RuntimePort = 60071;

    private readonly ITestOutputHelper _output;
    private readonly ConcurrentBag<string> _workerProxyLogs = new();
    private readonly ConcurrentBag<string> _mockWorkerLogs = new();
    private readonly ConcurrentBag<string> _runtimeLogs = new();

    private Process _workerProxyProcess;
    private Process _mockWorkerProcess;
    private Process _runtimeProcess;
    private HttpClient _httpClient;
    private string _scriptRootPath;

    public ExternalWorkerEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        string repoRoot = FindRepoRoot();
        _output.WriteLine($"Repository root: {repoRoot}");

        // Create a temporary script root directory (the runtime requires one even when
        // external worker mode supplies metadata over gRPC).
        _scriptRootPath = Path.Combine(Path.GetTempPath(), $"FunctionsE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scriptRootPath);

        // Write a minimal host.json so the runtime doesn't create a default one with
        // extension bundles (which require an internet download and cause startup failures).
        File.WriteAllText(
            Path.Combine(_scriptRootPath, "host.json"),
            "{\"version\":\"2.0\"}");

        _output.WriteLine($"Script root: {_scriptRootPath}");

        string workerProxyDll = FindBuiltDll(repoRoot, "src", "Functions.WorkerProxy");
        string mockWorkerDll = FindBuiltDll(repoRoot, "tools", "compute-separation-harness", "MockWorker");
        string runtimeDll = FindBuiltDll(repoRoot, "src", "WebJobs.Script.WebHost");

        _output.WriteLine($"Worker proxy DLL: {workerProxyDll}");
        _output.WriteLine($"MockWorker DLL: {mockWorkerDll}");
        _output.WriteLine($"Runtime DLL: {runtimeDll}");

        // 1. Start the worker proxy relay.
        _workerProxyProcess = StartManagedProcess(
            "dotnet",
            $"\"{workerProxyDll}\" --runtime-grpc-port {RuntimeGrpcPort} --worker-grpc-port {WorkerGrpcPort} --http-proxy-port {HttpProxyPort}",
            _workerProxyLogs,
            "WorkerProxy");

        // Give the worker proxy time to bind its ports.
        await Task.Delay(3000);
        EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", _workerProxyLogs);

        // 2. Start the mock worker.
        _mockWorkerProcess = StartManagedProcess(
            "dotnet",
            $"\"{mockWorkerDll}\" --grpc-endpoint http://localhost:{WorkerGrpcPort}",
            _mockWorkerLogs,
            "MockWorker");

        // Give the worker time to connect to the worker proxy.
        await Task.Delay(3000);
        EnsureProcessRunning(_mockWorkerProcess, "MockWorker", _mockWorkerLogs);

        // 3. Start the Functions runtime in external-worker mode.
        _runtimeProcess = StartManagedProcess(
            "dotnet",
            $"\"{runtimeDll}\" --urls http://localhost:{RuntimePort}",
            _runtimeLogs,
            "Runtime",
            new Dictionary<string, string>
            {
                ["FUNCTIONS_WORKER_EXTERNAL_ENABLED"] = "true",
                ["FUNCTIONS_WORKER_EXTERNAL_GRPC_ENDPOINT"] = $"http://localhost:{RuntimeGrpcPort}",
                ["FUNCTIONS_WORKER_RUNTIME"] = "node",
                ["AzureWebJobsScriptRoot"] = _scriptRootPath,
                ["AzureWebJobsStorage"] = "",
                ["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development"
            });

        // 4. Wait for the runtime to be ready.
        _httpClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{RuntimePort}") };

        await WaitForHostReadyAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task HttpTrigger_ThroughWorkerProxy_ReturnsExpectedResponse()
    {
        HttpResponseMessage response = await _httpClient.GetAsync("/api/HttpTrigger");

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
        _httpClient?.Dispose();

        KillProcess(_runtimeProcess, "Runtime");
        KillProcess(_mockWorkerProcess, "MockWorker");
        KillProcess(_workerProxyProcess, "WorkerProxy");

        TryDeleteDirectory(_scriptRootPath);

        GC.SuppressFinalize(this);
    }

    private static string FindRepoRoot()
    {
        // Walk up from the test assembly location to find the solution file.
        string dir = Path.GetDirectoryName(typeof(ExternalWorkerEndToEndTests).Assembly.Location);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "WebJobs.Script.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not find the repository root (no WebJobs.Script.sln found in parent directories).");
    }

    private static string FindBuiltDll(string repoRoot, params string[] projectPathSegments)
    {
        // The repo uses ArtifactsPath (Directory.Build.props) so build output goes to
        // {repoRoot}/out/bin/{ProjectName}/{config}/ rather than the project-local bin/.
        string projectName = projectPathSegments.Last();

        var knownAssemblyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Functions.WorkerProxy"] = "Microsoft.Azure.Functions.WorkerProxy.dll",
            ["MockWorker"] = "MockWorker.dll",
            ["WebJobs.Script.WebHost"] = "Microsoft.Azure.WebJobs.Script.WebHost.dll"
        };

        string dllName = knownAssemblyNames.TryGetValue(projectName, out string name) ? name : $"{projectName}.dll";

        // Map project directory names to their artifact folder names.
        var artifactFolderNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Functions.WorkerProxy"] = "Functions.WorkerProxy",
            ["MockWorker"] = "MockWorker",
            ["WebJobs.Script.WebHost"] = "WebJobs.Script.WebHost"
        };

        string artifactFolder = artifactFolderNames.TryGetValue(projectName, out string folder) ? folder : projectName;
        string binDir = Path.Combine(repoRoot, "out", "bin", artifactFolder);

        if (!Directory.Exists(binDir))
        {
            string projectDir = Path.Combine(new[] { repoRoot }.Concat(projectPathSegments).ToArray());
            throw new InvalidOperationException(
                $"Build output directory not found: {binDir}. " +
                $"Build the project first: dotnet build {projectDir}");
        }

        string[] candidates = Directory.GetFiles(binDir, dllName, SearchOption.AllDirectories);
        if (candidates.Length == 0)
        {
            string projectDir = Path.Combine(new[] { repoRoot }.Concat(projectPathSegments).ToArray());
            throw new InvalidOperationException(
                $"Could not find {dllName} under {binDir}. " +
                $"Build the project first: dotnet build {projectDir}");
        }

        // Prefer the config that matches the current test build (debug/release).
        string preferredConfig = TestHelpers.BuildConfig;
        string preferred = candidates.FirstOrDefault(c =>
            c.Contains(Path.DirectorySeparatorChar + preferredConfig + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        return preferred ?? candidates
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .First();
    }

    private Process StartManagedProcess(
        string fileName,
        string arguments,
        ConcurrentBag<string> logSink,
        string label,
        IDictionary<string, string> environmentVariables = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ErrorDialog = false
        };

        if (environmentVariables is not null)
        {
            foreach (var kvp in environmentVariables)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                string line = $"[{label}] {e.Data}";
                logSink.Add(line);
                _output.WriteLine(line);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                string line = $"[{label}:ERR] {e.Data}";
                logSink.Add(line);
                _output.WriteLine(line);
            }
        };

        _output.WriteLine($"Starting {label}: {fileName} {arguments}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private void EnsureProcessRunning(Process process, string label, ConcurrentBag<string> logSink)
    {
        if (process.HasExited)
        {
            string logs = string.Join(Environment.NewLine, logSink);
            throw new InvalidOperationException(
                $"{label} process exited prematurely with code {process.ExitCode}. Logs:{Environment.NewLine}{logs}");
        }
    }

    private async Task WaitForHostReadyAsync(TimeSpan timeout)
    {
        _output.WriteLine($"Waiting for runtime to become ready (timeout: {timeout})...");
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", _workerProxyLogs);
            EnsureProcessRunning(_mockWorkerProcess, "MockWorker", _mockWorkerLogs);
            EnsureProcessRunning(_runtimeProcess, "Runtime", _runtimeLogs);

            try
            {
                using var response = await _httpClient.GetAsync("/");

                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
                {
                    _output.WriteLine($"Runtime is ready after {sw.Elapsed.TotalSeconds:F1}s.");

                    return;
                }
            }
            catch
            {
                // Host not ready yet — will retry.
            }

            await Task.Delay(2000);
        }

        // Dump process logs on timeout for diagnostics.
        DumpAllLogs();

        throw new TimeoutException(
            $"Runtime did not become ready within {timeout.TotalSeconds}s.");
    }

    private void DumpAllLogs()
    {
        _output.WriteLine("=== WorkerProxy Logs ===");
        foreach (string log in _workerProxyLogs)
        {
            _output.WriteLine(log);
        }

        _output.WriteLine("=== MockWorker Logs ===");
        foreach (string log in _mockWorkerLogs)
        {
            _output.WriteLine(log);
        }

        _output.WriteLine("=== Runtime Logs ===");
        foreach (string log in _runtimeLogs)
        {
            _output.WriteLine(log);
        }
    }

    private void KillProcess(Process process, string label)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                _output.WriteLine($"Stopping {label} (PID {process.Id})...");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: failed to stop {label}: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
