// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// Shared helpers for compute separation E2E tests that launch child processes
/// (worker proxy, mock worker, etc.).
/// </summary>
internal static class ComputeSeparationTestHelpers
{
    private const string WorkerProxyAudience = "worker-proxy-compute-separation";
    private static readonly byte[] WorkerProxySigningKeyBytes = TestHelpers.GenerateKeyBytes();
    private static readonly string WorkerProxySigningKey = Convert.ToBase64String(WorkerProxySigningKeyBytes);

    public static string FindRepoRoot()
    {
        string dir = Path.GetDirectoryName(typeof(ComputeSeparationTestHelpers).Assembly.Location);

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

    public static string FindBuiltDll(string repoRoot, params string[] projectPathSegments)
    {
        string projectName = projectPathSegments.Last();

        var knownAssemblyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Functions.WorkerProxy"] = "Microsoft.Azure.Functions.WorkerProxy.dll",
            ["MockWorker"] = "MockWorker.dll",
            ["WebJobs.Script.WebHost"] = "Microsoft.Azure.WebJobs.Script.WebHost.dll"
        };

        string dllName = knownAssemblyNames.TryGetValue(projectName, out string name) ? name : $"{projectName}.dll";

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

        string preferredConfig = TestHelpers.BuildConfig;
        string preferred = candidates.FirstOrDefault(c =>
            c.Contains(Path.DirectorySeparatorChar + preferredConfig + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        return preferred ?? candidates
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .First();
    }

    public static Process StartManagedProcess(
        ITestOutputHelper output,
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
                output.WriteLine(line);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                string line = $"[{label}:ERR] {e.Data}";
                logSink.Add(line);
                output.WriteLine(line);
            }
        };

        output.WriteLine($"Starting {label}: {fileName} {arguments}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    public static IDictionary<string, string> GetWorkerProxyAuthEnvironment()
        => new Dictionary<string, string>
        {
            [EnvironmentSettingNames.ContainerEncryptionKey] = WorkerProxySigningKey,
            [EnvironmentSettingNames.WebsitePodName] = WorkerProxyAudience
        };

    public static HttpClient CreateAuthenticatedWorkerProxyClient(int managementPort)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{managementPort}")
        };

        client.DefaultRequestHeaders.Add(ScriptConstants.SiteTokenHeaderName, CreateWorkerProxySiteToken());
        return client;
    }

    public static HttpContent CreateJsonContent(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    public static HttpContent CreateWorkerAssignRequestContent(
        string workerRuntime = "node",
        string functionAppName = "test-compute-sep-app",
        int functionAppId = 1234,
        string functionGroupName = "http",
        bool isAlwaysReady = false,
        string functionAppDirectory = "/home/site/wwwroot")
    {
        var assignPayload = new
        {
            FunctionAppName = functionAppName,
            FunctionAppId = functionAppId,
            FunctionGroupName = functionGroupName,
            IsAlwaysReady = isAlwaysReady,
            Environment = new Dictionary<string, string>
            {
                ["FUNCTIONS_WORKER_RUNTIME"] = workerRuntime
            },
            FunctionAppDirectory = functionAppDirectory
        };

        return CreateJsonContent(assignPayload);
    }

    public static object CreateWorkerLinkRequest(string workerId, int runtimeGrpcPort, int httpProxyPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        return new
        {
            WorkerPodName = workerId,
            WorkerHttpEndpoint = $"http://localhost:{httpProxyPort}",
            WorkerGrpcEndpoint = $"http://localhost:{runtimeGrpcPort}",
            WorkerContainerEncryptionKey = WorkerProxySigningKey
        };
    }

    public static void EnsureProcessRunning(Process process, string label, ConcurrentBag<string> logSink)
    {
        if (process.HasExited)
        {
            string logs = string.Join(Environment.NewLine, logSink);
            throw new InvalidOperationException(
                $"{label} process exited prematurely with code {process.ExitCode}. Logs:{Environment.NewLine}{logs}");
        }
    }

    public static void KillProcess(ITestOutputHelper output, Process process, string label)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                output.WriteLine($"Stopping {label} (PID {process.Id})...");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"Warning: failed to stop {label}: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    public static void TryDeleteDirectory(string path)
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

    /// <summary>
    /// Polls <c>GET /admin/worker/ready</c> on the worker proxy management endpoint until it returns 200,
    /// replacing fixed <c>Task.Delay</c> waits after process startup.
    /// </summary>
    public static async Task WaitForWorkerProxyReadyAsync(int managementPort, ITestOutputHelper output, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{managementPort}") };
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            try
            {
                var response = await client.GetAsync("/admin/worker/ready");
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    output.WriteLine($"Worker proxy ready after {sw.Elapsed.TotalSeconds:F1}s.");
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Worker proxy did not become ready within {timeout.Value.TotalSeconds}s.");
    }

    private static string CreateWorkerProxySiteToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Audience = WorkerProxyAudience,
            Issuer = ScriptConstants.LegionCoreUri,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(WorkerProxySigningKeyBytes),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
