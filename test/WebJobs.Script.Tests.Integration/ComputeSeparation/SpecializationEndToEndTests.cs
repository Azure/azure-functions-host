// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Tests.Integration.Fixtures;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.ComputeSeparation;

/// <summary>
/// End-to-end test for the specialization flow in external worker mode.
/// Starts the runtime in Flex Consumption placeholder mode, specializes via
/// <c>/admin/instance/assign</c>, links a worker via <c>/admin/workers/link</c>,
/// and invokes a function through the mock worker.
/// </summary>
[Trait(TestTraits.Category, TestTraits.EndToEnd)]
[Trait(TestTraits.Group, nameof(ExternalWorkerEndToEndTests))]
public class SpecializationEndToEndTests : IAsyncLifetime, IDisposable, IClassFixture<AzuriteFixture>
{
    private const int RuntimeGrpcPort = 60071;
    private const int WorkerGrpcPort = 60072;
    private const int HttpProxyPort = 60073;
    private const int ManagementPort = 60074;

    private readonly ITestOutputHelper _output;
    private readonly AzuriteFixture _azurite;
    private readonly string _encryptionKey = Convert.ToBase64String(TestHelpers.GenerateKeyBytes());
    private readonly string _testRootPath = Path.Combine(Path.GetTempPath(), $"FunctionsSpecE2E_{Guid.NewGuid():N}");

    private Process _workerProxyProcess;
    private Process _mockWorkerProcess;
    private TestEnvironment _environment;
    private TestLoggerProvider _loggerProvider;
    private IHost _webHost;
    private HttpClient _httpClient;

    // Process-level env vars that must be set for DI registration (SystemEnvironment.Instance checks).
    // Cleaned up in Dispose.
    private static readonly string[] ProcessEnvVars =
    [
        EnvironmentSettingNames.AzureWebsitePlaceholderMode,
        EnvironmentSettingNames.AzureWebsiteSku,
        EnvironmentSettingNames.FunctionsWorkerExternalEnabled,
        "AzureWebJobsStorage",
    ];

    public SpecializationEndToEndTests(ITestOutputHelper output, AzuriteFixture azurite)
    {
        _output = output;
        _azurite = azurite;
        StandbyManager.ResetChangeToken();
    }

    public async Task InitializeAsync()
    {
        string repoRoot = ComputeSeparationTestHelpers.FindRepoRoot();

        // 1. Start worker proxy + mock worker child processes.
        string workerProxyDll = ComputeSeparationTestHelpers.FindBuiltDll(repoRoot, "src", "Functions.WorkerProxy");
        string mockWorkerDll = ComputeSeparationTestHelpers.FindBuiltDll(repoRoot, "tools", "ComputeSeparation", "MockWorker");

        _workerProxyProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output, "dotnet",
            $"\"{workerProxyDll}\" --runtime-grpc-port {RuntimeGrpcPort} --worker-grpc-port {WorkerGrpcPort} --http-proxy-port {HttpProxyPort} --management-port {ManagementPort}",
            new(), "WorkerProxy",
            environmentVariables: ComputeSeparationTestHelpers.GetWorkerProxyAuthEnvironment());

        await Task.Delay(2000);
        ComputeSeparationTestHelpers.EnsureProcessRunning(_workerProxyProcess, "WorkerProxy", new());

        _mockWorkerProcess = ComputeSeparationTestHelpers.StartManagedProcess(
            _output, "dotnet",
            $"\"{mockWorkerDll}\" --grpc-endpoint http://localhost:{WorkerGrpcPort}",
            new(), "MockWorker");

        await ComputeSeparationTestHelpers.WaitForWorkerProxyReadyAsync(ManagementPort, _output);

        // 2. Configure environment.
        //    Process-level vars are needed for SystemEnvironment.Instance checks during DI.
        //    TestEnvironment is injected as IEnvironment for runtime behavior.
        //    InMemoryCollection in IConfiguration ensures config reads also see these values.
        string storageConnection = _azurite.GetConnectionString();

        Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);
        Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true");
        Environment.SetEnvironmentVariable("AzureWebJobsStorage", storageConnection);

        _environment = new TestEnvironment(new Dictionary<string, string>
        {
            { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
            { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
            { EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku },
            { EnvironmentSettingNames.ContainerEncryptionKey, _encryptionKey },
            { EnvironmentSettingNames.AzureWebsiteName, "test-compute-sep-app" },
            { EnvironmentSettingNames.AzureWebsiteInstanceId, Guid.NewGuid().ToString() },
            { EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true" },
            { EnvironmentSettingNames.FunctionWorkerRuntime, "node" },
            { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" },
            { "AzureWebJobsStorage", storageConnection },
        });

        // 3. Build and start the host via TestServer.
        var uniqueTestPath = Path.Combine(_testRootPath, Guid.NewGuid().ToString());
        var scriptRootPath = Path.Combine(uniqueTestPath, "wwwroot");
        FileUtility.EnsureDirectoryExists(scriptRootPath);

        _loggerProvider = new TestLoggerProvider();

        _webHost = new HostBuilder()
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder.UseTestServer();

                webHostBuilder.ConfigureServices(services =>
                {
                    services.ConfigureAll<ScriptApplicationHostOptions>(o =>
                    {
                        o.IsSelfHost = true;
                        o.LogPath = Path.Combine(uniqueTestPath, "logs");
                        o.SecretsPath = Path.Combine(uniqueTestPath, "secrets");
                        o.ScriptPath = scriptRootPath;
                    });

                    services.AddSingleton<IEnvironment>(_ => _environment);
                    services.AddSingleton<IMetricsLogger>(_ => new TestMetricsLogger());
                    services.AddSingleton<ILinuxConsumptionMetricsTracker>(_ => new TestMetricsTracker());
                });

                webHostBuilder.UseStartup<Startup>();
                webHostBuilder.ConfigureAppConfiguration((_, c) =>
                {
                    var source = c.Sources.OfType<WebScriptHostConfigurationSource>().SingleOrDefault();
                    if (source is not null)
                    {
                        c.Sources.Remove(source);
                    }

                    c.AddTestSettings();
                    c.AddInMemoryCollection([
                        KeyValuePair.Create("AzureWebJobsStorage", storageConnection),
                        KeyValuePair.Create(EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true"),
                    ]);
                });
            })
            .ConfigureLogging(c =>
            {
                c.AddProvider(_loggerProvider);
                c.AddFilter((cat, lev) => true);
            })
            .ConfigureScriptHostLogging(b => b.AddProvider(_loggerProvider))
            .ConfigureScriptHostServices(s => s.PostConfigure<HttpOptions>(o => o.DynamicThrottlesEnabled = false))
            .ConfigureScriptHostAppConfiguration(c =>
            {
                c.AddInMemoryCollection([KeyValuePair.Create("AzureWebJobsStorage", storageConnection)]);
            })
            .Build();

        await _webHost.StartAsync();

        _httpClient = _webHost.GetTestClient();
        _httpClient.BaseAddress = new Uri("https://localhost/");

        Assert.True(_environment.IsPlaceholderModeEnabled(), "Host should be in placeholder mode.");
        _output.WriteLine("Runtime started in placeholder mode.");
    }

    [Fact]
    public async Task SpecializationFlow_AssignThenLinkThenInvoke()
    {
        var secretManager = _webHost.Services.GetService<ISecretManagerProvider>().Current;
        string masterKey = (await secretManager.GetHostSecretsAsync()).MasterKey;

        // 2. Assign the worker — drives init + specialize + metadata prefetch
        //    so cached responses are ready when the runtime links.
        using var proxyClient = ComputeSeparationTestHelpers.CreateAuthenticatedWorkerProxyClient(ManagementPort);
        var workerAssignResponse = await proxyClient.PostAsync("/admin/worker/assign",
            new StringContent(
                JsonConvert.SerializeObject(new { environment = new { FUNCTIONS_WORKER_RUNTIME = "node" }, functionAppDirectory = "/home/site/wwwroot" }),
                Encoding.UTF8, "application/json"));
        _output.WriteLine($"Worker assign response: {workerAssignResponse.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, workerAssignResponse.StatusCode);

        // 3. Specialize via /admin/instance/assign with encrypted context.
        var assignmentContext = new HostAssignmentContext
        {
            SiteId = 1234,
            SiteName = "test-compute-sep-app",
            Environment = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true" },
                { EnvironmentSettingNames.FunctionWorkerRuntime, "node" },
                { EnvironmentSettingNames.AzureWebsiteName, "test-compute-sep-app" },
            }
        };

        string encryptedContext = EncryptionHelper.Encrypt(
            JsonConvert.SerializeObject(assignmentContext),
            Convert.FromBase64String(_encryptionKey));

        var assignResponse = await SendAdminRequest(
            masterKey, HttpMethod.Post, "admin/instance/assign",
            new { encryptedContext });

        _output.WriteLine($"Assign response: {assignResponse.StatusCode}");
        Assert.Equal(HttpStatusCode.Accepted, assignResponse.StatusCode);

        // 4. Link a worker.
        var sw = Stopwatch.StartNew();
        HttpResponseMessage linkResponse = null;

        while (sw.Elapsed < TimeSpan.FromMinutes(2))
        {
            linkResponse = await SendAdminRequest(
                masterKey, HttpMethod.Put, "admin/workers/w_spec_e2e01",
                new
                {
                    workerId = "w_spec_e2e01",
                    podName = "worker-pod-spec-e2e",
                    grpcEndpoint = $"http://localhost:{RuntimeGrpcPort}",
                    podKey = "test-key"
                });

            if (linkResponse.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            _output.WriteLine($"Link returned {linkResponse.StatusCode} at {sw.Elapsed.TotalSeconds:F1}s, retrying...");
            await Task.Delay(1000);
        }

        _output.WriteLine($"Link response: {linkResponse?.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, linkResponse?.StatusCode);

        // 5. Wait for host to reach Running state.
        await TestHelpers.Await(
            () =>
            {
                var manager = _webHost.Services.GetService<IScriptHostManager>();
                _output.WriteLine($"Host state: {manager.State} ({sw.Elapsed.TotalSeconds:F1}s)");
                return manager.State == ScriptHostState.Running;
            },
            timeout: 120_000,
            pollingInterval: 1000,
            userMessageCallback: () => string.Join(Environment.NewLine,
                _loggerProvider.GetAllLogMessages()
                    .Where(m => m.FormattedMessage is not null)
                    .TakeLast(50)
                    .Select(m => $"[{m.Timestamp:HH:mm:ss.fff}] {m.FormattedMessage}")));

        _output.WriteLine("Host is running.");

        // 6. Invoke a function.
        var invokeResponse = await _httpClient.GetAsync("/api/HttpTrigger");
        string invokeBody = await invokeResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Invoke: {invokeResponse.StatusCode} — {invokeBody}");

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
        _httpClient?.Dispose();

        if (_webHost is not null)
        {
            try { _webHost.StopAsync().GetAwaiter().GetResult(); } catch { }
            _webHost.Dispose();
        }

        _loggerProvider?.Dispose();
        ComputeSeparationTestHelpers.KillProcess(_output, _mockWorkerProcess, "MockWorker");
        ComputeSeparationTestHelpers.KillProcess(_output, _workerProxyProcess, "WorkerProxy");
        ComputeSeparationTestHelpers.TryDeleteDirectory(_testRootPath);

        foreach (string key in ProcessEnvVars)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        GC.SuppressFinalize(this);
    }

    private async Task<HttpResponseMessage> SendAdminRequest(
        string masterKey, HttpMethod method, string path, object body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, masterKey);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request);
    }
}
