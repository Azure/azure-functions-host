// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class FunctionRpcRelayTests : IDisposable
{
    private readonly WorkerPodStateManager _stateManager;
    private readonly string _tempDir;

    public FunctionRpcRelayTests()
    {
        _stateManager = new WorkerPodStateManager(new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"));
        _tempDir = Path.Combine(Path.GetTempPath(), $"RelayTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private FunctionRpcRelay CreateRelay(string? hostJsonPath = null)
    {
        var options = new RelayOptions(50051, 50052, 50053, hostJsonPath, "http://localhost:50053", "test-pod");
        return new FunctionRpcRelay(options, NullLogger<FunctionRpcRelay>.Instance, _stateManager);
    }

    private static StreamingMessage CreateWorkerInitResponse(Dictionary<string, string>? capabilities = null)
    {
        var message = new StreamingMessage
        {
            WorkerInitResponse = new WorkerInitResponse()
        };

        if (capabilities is not null)
        {
            foreach (var kvp in capabilities)
            {
                message.WorkerInitResponse.Capabilities[kvp.Key] = kvp.Value;
            }
        }

        return message;
    }

    // --- InjectHostJson tests ---

    [Fact]
    public void InjectHostJson_FromFunctionAppDirectory()
    {
        string hostJsonContent = """{"version":"2.0","logging":{"logLevel":{"default":"Information"}}}""";
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), hostJsonContent);

        var relay = CreateRelay();
        var message = CreateWorkerInitResponse();

        relay.InjectHostJson(message, _tempDir);

        Assert.Equal(hostJsonContent, message.WorkerInitResponse.Capabilities["host_configuration_json"]);
    }

    [Fact]
    public void InjectHostJson_FromExplicitHostJsonPath()
    {
        string explicitPath = Path.Combine(_tempDir, "custom-host.json");
        string hostJsonContent = """{"version":"2.0","customSetting":true}""";
        File.WriteAllText(explicitPath, hostJsonContent);

        var relay = CreateRelay(hostJsonPath: explicitPath);
        var message = CreateWorkerInitResponse();

        relay.InjectHostJson(message, functionAppDirectory: "/nonexistent");

        Assert.Equal(hostJsonContent, message.WorkerInitResponse.Capabilities["host_configuration_json"]);
    }

    [Fact]
    public void InjectHostJson_FunctionAppDirectory_TakesPrecedenceOverExplicitPath()
    {
        string appDirContent = """{"version":"2.0","source":"appDir"}""";
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), appDirContent);

        string explicitPath = Path.Combine(_tempDir, "custom-host.json");
        File.WriteAllText(explicitPath, """{"version":"2.0","source":"explicit"}""");

        var relay = CreateRelay(hostJsonPath: explicitPath);
        var message = CreateWorkerInitResponse();

        relay.InjectHostJson(message, _tempDir);

        Assert.Equal(appDirContent, message.WorkerInitResponse.Capabilities["host_configuration_json"]);
    }

    [Fact]
    public void InjectHostJson_NoHostJsonAnywhere_CapabilityNotSet()
    {
        var relay = CreateRelay();
        var message = CreateWorkerInitResponse();

        relay.InjectHostJson(message, functionAppDirectory: "/nonexistent");

        Assert.False(message.WorkerInitResponse.Capabilities.ContainsKey("host_configuration_json"));
    }

    [Fact]
    public void InjectHostJson_NullFunctionAppDirectory_FallsBackToExplicitPath()
    {
        string explicitPath = Path.Combine(_tempDir, "host.json");
        string hostJsonContent = """{"version":"2.0"}""";
        File.WriteAllText(explicitPath, hostJsonContent);

        var relay = CreateRelay(hostJsonPath: explicitPath);
        var message = CreateWorkerInitResponse();

        relay.InjectHostJson(message, functionAppDirectory: null!);

        Assert.Equal(hostJsonContent, message.WorkerInitResponse.Capabilities["host_configuration_json"]);
    }

    [Fact]
    public void InjectHostJson_WrongMessageType_NoOp()
    {
        var relay = CreateRelay();
        var message = new StreamingMessage { WorkerInitRequest = new WorkerInitRequest() };

        relay.InjectHostJson(message, _tempDir);

        Assert.Equal(StreamingMessage.ContentOneofCase.WorkerInitRequest, message.ContentCase);
    }

    // --- HttpUri rewrite tests ---

    [Fact]
    public void InjectHostJson_DoesNotAffectHttpUri()
    {
        var relay = CreateRelay();
        var message = CreateWorkerInitResponse(new Dictionary<string, string>
        {
            ["HttpUri"] = "http://original:8080"
        });

        relay.InjectHostJson(message, "/nonexistent");

        Assert.Equal("http://original:8080", message.WorkerInitResponse.Capabilities["HttpUri"]);
    }

    // --- Existing tests ---

    [Fact]
    public async Task SendDrainRequestToRuntimeAsync_WritesMessageToChannel()
    {
        var relay = CreateRelay();
        await relay.SendDrainRequestToRuntimeAsync();
    }

    [Fact]
    public async Task SendDrainRequestToRuntimeAsync_MultipleCalls_AllSucceed()
    {
        var relay = CreateRelay();
        await relay.SendDrainRequestToRuntimeAsync();
        await relay.SendDrainRequestToRuntimeAsync();
        await relay.SendDrainRequestToRuntimeAsync();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionRpcRelay(null!, NullLogger<FunctionRpcRelay>.Instance, _stateManager));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var options = new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod");
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionRpcRelay(options, null!, _stateManager));
    }

    [Fact]
    public void Constructor_NullStateManager_Throws()
    {
        var options = new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod");
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionRpcRelay(options, NullLogger<FunctionRpcRelay>.Instance, null!));
    }

    [Fact]
    public void InitialState_IsNone()
    {
        Assert.Equal(WorkerPodStatus.None, _stateManager.CurrentStatus);
    }

    // --- Specialization reentrancy tests ---

    [Fact]
    public async Task SpecializeWorkerAsync_SecondCall_ThrowsInvalidOperation()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        // Start a worker simulation that responds to the first init request.
        var workerTask = SimulateWorkerAsync(relay);

        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None));

        Assert.Contains("already been initiated", ex.Message);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_ConcurrentCalls_OnlyOneSucceeds()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay);

        var task1 = relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);
        var task2 = relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        var results = await Task.WhenAll(
            CaptureOutcomeAsync(task1),
            CaptureOutcomeAsync(task2));

        int successes = results.Count(r => r is null);
        int conflicts = results.Count(r => r is InvalidOperationException);

        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);
    }

    // --- Stale cache prevention tests ---

    [Fact]
    public async Task SpecializeWorkerAsync_EnvReloadFailure_CachedResponseIsNull()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        // Simulate a worker that returns success for init but failure for env reload.
        var workerTask = SimulateWorkerAsync(relay, envReloadStatus: StatusResult.Types.Status.Failure);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None));

        Assert.Null(relay._cachedWorkerInitResponse);
        Assert.Null(relay._cachedFunctionMetadataResponse);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_Success_CachedResponseIsPopulated()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay);

        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        Assert.NotNull(relay._cachedWorkerInitResponse);
        Assert.NotNull(relay._cachedFunctionMetadataResponse);

        // Verify HttpUri was rewritten to the proxy endpoint.
        Assert.Equal("http://localhost:50053",
            relay._cachedWorkerInitResponse.WorkerInitResponse.Capabilities["HttpUri"]);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_EnvReloadFailure_SpecializationCompletedSignalsFailure()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay, envReloadStatus: StatusResult.Types.Status.Failure);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None));
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerInitFailure_ThrowsAndCacheIsNull()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay, initStatus: StatusResult.Types.Status.Failure);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None));

        Assert.Contains("initialization failed", ex.Message);
        Assert.Null(relay._cachedWorkerInitResponse);
        Assert.Null(relay._cachedFunctionMetadataResponse);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerInitFailure_DoesNotSendEnvReload()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay, initStatus: StatusResult.Types.Status.Failure);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None));

        // The channel should have no more messages — env reload was never sent.
        Assert.False(relay._toWorker.Reader.TryRead(out _));
    }

    [Fact]
    public async Task SpecializeWorkerAsync_Cancellation_ClearsPendingWorkerResponse()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        using var cts = new CancellationTokenSource();

        // Start specialization — it will send WorkerInitRequest and wait for a response.
        var specializeTask = relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", cts.Token);

        // Read the WorkerInitRequest from the channel (so it's consumed) but don't respond.
        var initRequest = await relay._toWorker.Reader.ReadAsync();
        Assert.Equal(StreamingMessage.ContentOneofCase.WorkerInitRequest, initRequest.ContentCase);

        // Cancel while waiting for the WorkerInitResponse.
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => specializeTask);

        // The pending TCS should be cleared so a late-arriving worker response
        // doesn't get swallowed by ReadInboundAsync.
        Assert.Null(relay._pendingWorkerResponse);
    }

    // --- Capability merge tests ---

    [Fact]
    public async Task SpecializeWorkerAsync_MergeStrategy_MergesCapabilities()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var initCaps = new Dictionary<string, string>
        {
            ["EnableUserCodeException"] = "True",
            ["SomeInitCap"] = "InitValue"
        };

        var reloadCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:9999",
            ["RpcHttpBodyOnly"] = "True",
            ["SomeInitCap"] = "OverriddenByReload"
        };

        var workerTask = SimulateWorkerAsync(relay, initCapabilities: initCaps, reloadCapabilities: reloadCaps);
        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        var cached = relay._cachedWorkerInitResponse!.WorkerInitResponse.Capabilities;

        // Init-only capability preserved.
        Assert.Equal("True", cached["EnableUserCodeException"]);
        // Reload capability merged in.
        Assert.Equal("True", cached["RpcHttpBodyOnly"]);
        // Reload overwrites init value.
        Assert.Equal("OverriddenByReload", cached["SomeInitCap"]);
        // HttpUri rewritten to proxy endpoint (not worker's value).
        Assert.Equal("http://localhost:50053", cached["HttpUri"]);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_ReplaceStrategy_ClearsAndReplacesCapabilities()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var initCaps = new Dictionary<string, string>
        {
            ["ShouldBeRemoved"] = "True",
            ["AlsoRemoved"] = "Yes"
        };

        var reloadCaps = new Dictionary<string, string>
        {
            ["OnlyThisSurvives"] = "True"
        };

        var workerTask = SimulateWorkerAsync(relay,
            initCapabilities: initCaps,
            reloadCapabilities: reloadCaps,
            reloadStrategy: FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy.Replace);

        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        var cached = relay._cachedWorkerInitResponse!.WorkerInitResponse.Capabilities;

        // Init-only capabilities should be cleared.
        Assert.False(cached.ContainsKey("ShouldBeRemoved"));
        Assert.False(cached.ContainsKey("AlsoRemoved"));
        // Reload capability present.
        Assert.Equal("True", cached["OnlyThisSurvives"]);
        // HttpUri still set by proxy (injected after merge).
        Assert.True(cached.ContainsKey("HttpUri"));
    }

    // --- RewriteHttpUri tests ---

    [Fact]
    public async Task SpecializeWorkerAsync_HttpUri_AlwaysSetEvenWhenWorkerOmitsIt()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        // Worker reports no HttpUri at all.
        var workerTask = SimulateWorkerAsync(relay);
        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        var cached = relay._cachedWorkerInitResponse!.WorkerInitResponse.Capabilities;
        Assert.Equal("http://localhost:50053", cached["HttpUri"]);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_HttpUri_OverwritesWorkerValue()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        // Worker reports its own HttpUri — proxy should overwrite it.
        var reloadCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:44567"
        };

        var workerTask = SimulateWorkerAsync(relay, reloadCapabilities: reloadCaps);
        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None);

        var cached = relay._cachedWorkerInitResponse!.WorkerInitResponse.Capabilities;
        Assert.Equal("http://localhost:50053", cached["HttpUri"]);
    }

    // --- WorkerHttpEndpoint capture tests ---
    // These verify the dynamic port advertised by the worker via its WorkerInitResponse
    // (or FunctionEnvironmentReloadResponse) "HttpUri" capability is correctly captured
    // into FunctionRpcRelay.WorkerHttpEndpoint, which the HTTP forwarding middleware uses
    // as the YARP destination. Real .NET-isolated, Python, and Node v4 workers all bind
    // to a kernel-assigned loopback port at startup and advertise it here, so losing this
    // value silently breaks HTTP invocation routing entirely.

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_CapturedFromInitResponse_MergeStrategy()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var initCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:5001"
        };

        var workerTask = SimulateWorkerAsync(relay, initCapabilities: initCaps);
        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Equal("http://localhost:5001", relay.WorkerHttpEndpoint);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_ReplaceStrategyWithoutHttpUri_DropsCapture()
    {
        // CapabilitiesUpdateStrategy.Replace means the reload-time set IS the new
        // capability set — anything not in it is intentionally gone. If the worker
        // advertised HttpUri at init time but omits it from a Replace reload, that's
        // the worker explicitly signalling "I no longer expose an HTTP endpoint" (e.g.,
        // a worker that dropped its in-process HTTP listener after specialization).
        // The proxy must respect that and leave WorkerHttpEndpoint null so requests
        // fall through to the override or 503, rather than continuing to route to a
        // listener that no longer exists.
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var initCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:5001",
            ["RawHttpBodyBytes"] = "true"
        };

        var reloadCaps = new Dictionary<string, string>
        {
            ["RpcHttpBodyOnly"] = "True"
        };

        var workerTask = SimulateWorkerAsync(relay,
            initCapabilities: initCaps,
            reloadCapabilities: reloadCaps,
            reloadStrategy: FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy.Replace);

        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Null(relay.WorkerHttpEndpoint);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_CapturedFromReloadCapabilities()
    {
        // Some workers (e.g., the Python HTTP v2 streaming path) advertise HttpUri
        // only after specialization, in the FunctionEnvironmentReloadResponse rather
        // than the initial WorkerInitResponse. The capture must work in that case too.
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var reloadCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:5002"
        };

        var workerTask = SimulateWorkerAsync(relay, reloadCapabilities: reloadCaps);
        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Equal("http://localhost:5002", relay.WorkerHttpEndpoint);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_ReloadValueWinsOverInit()
    {
        // If the worker advertises HttpUri at both init and reload time, the reload
        // value is more recent and therefore authoritative. This mirrors the merge
        // semantics applied to every other capability.
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var initCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:5001"
        };

        var reloadCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = "http://localhost:5099"
        };

        var workerTask = SimulateWorkerAsync(relay, initCapabilities: initCaps, reloadCapabilities: reloadCaps);
        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Equal("http://localhost:5099", relay.WorkerHttpEndpoint);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_NullWhenWorkerNeverAdvertisesIt()
    {
        // Workers that don't support HttpUri at all (Java, PowerShell, Node v3, .NET
        // isolated without the AspNetCore extension) should leave WorkerHttpEndpoint
        // null. The middleware then falls back to the --worker-http-endpoint override
        // or returns 503 if no override was provided.
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay);
        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Null(relay.WorkerHttpEndpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_NullWhenWorkerAdvertisesBlankValue(string blankValue)
    {
        // Defensive: a worker that advertises an empty/whitespace HttpUri is treated
        // as if it advertised nothing, rather than producing a routing destination of
        // "" that would later cause forwarder.SendAsync to throw.
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var initCaps = new Dictionary<string, string>
        {
            ["HttpUri"] = blankValue
        };

        var workerTask = SimulateWorkerAsync(relay, initCapabilities: initCaps);
        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Null(relay.WorkerHttpEndpoint);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_WorkerHttpEndpoint_RewriteWithoutHttpUri_ClearsPreviouslyCapturedValue()
    {
        // Defense-in-depth regression for the capture-clear contract: the rewrite path
        // must reset _workerHttpEndpoint when the post-merge capabilities don't include
        // HttpUri, even if a previous run had captured a value. Today there's only one
        // call site so this can't happen via SpecializeWorkerAsync alone, but pre-seeding
        // the field simulates the "second specialization" / "pre-merge capture added by
        // a future refactor" scenarios. Without the explicit `_workerHttpEndpoint = null`
        // in RewriteHttpUri's else branch, this test would silently retain "http://stale".
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();
        relay._workerHttpEndpoint = "http://stale-from-previous-run:9999";

        var workerTask = SimulateWorkerAsync(relay);
        await Task.WhenAll(
            relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/app", CancellationToken.None),
            workerTask);

        Assert.Null(relay.WorkerHttpEndpoint);
    }

    // --- Env var forwarding tests ---

    [Fact]
    public async Task SpecializeWorkerAsync_EnvironmentVariables_ForwardedToWorker()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        FunctionEnvironmentReloadRequest? capturedRequest = null;
        var workerTask = SimulateWorkerAsync(relay, onEnvReload: req => capturedRequest = req);

        var envVars = new Dictionary<string, string>
        {
            ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated",
            ["WEBSITE_SITE_NAME"] = "myapp",
            ["CUSTOM_SETTING"] = "custom_value"
        };

        await relay.SpecializeWorkerAsync(envVars, "/home/site/wwwroot", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("dotnet-isolated", capturedRequest!.EnvironmentVariables["FUNCTIONS_WORKER_RUNTIME"]);
        Assert.Equal("myapp", capturedRequest.EnvironmentVariables["WEBSITE_SITE_NAME"]);
        Assert.Equal("custom_value", capturedRequest.EnvironmentVariables["CUSTOM_SETTING"]);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_FunctionsApplicationDirectory_InjectedIntoEnvReload()
    {
        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        FunctionEnvironmentReloadRequest? capturedRequest = null;
        var workerTask = SimulateWorkerAsync(relay, onEnvReload: req => capturedRequest = req);

        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), "/custom/app/path", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("/custom/app/path", capturedRequest!.FunctionAppDirectory);
        Assert.Equal("/custom/app/path", capturedRequest.EnvironmentVariables["FUNCTIONS_APPLICATION_DIRECTORY"]);
    }

    [Fact]
    public async Task SpecializeWorkerAsync_HostJson_InjectedIntoCachedResponse()
    {
        string hostJsonContent = """{"version":"2.0","extensions":{"http":{"routePrefix":""}}}""";
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), hostJsonContent);

        var relay = CreateRelay();
        relay._workerConnected.TrySetResult();

        var workerTask = SimulateWorkerAsync(relay);
        await relay.SpecializeWorkerAsync(new Dictionary<string, string>(), _tempDir, CancellationToken.None);

        var cached = relay._cachedWorkerInitResponse!.WorkerInitResponse.Capabilities;
        Assert.Equal(hostJsonContent, cached["host_configuration_json"]);
    }

    // --- Helper methods ---

    /// <summary>
    /// Simulates a worker responding to the relay's SpecializeWorkerAsync sequence.
    /// Reads requests from the _toWorker channel and completes _pendingWorkerResponse.
    /// </summary>
    private static async Task SimulateWorkerAsync(
        FunctionRpcRelay relay,
        StatusResult.Types.Status initStatus = StatusResult.Types.Status.Success,
        StatusResult.Types.Status envReloadStatus = StatusResult.Types.Status.Success,
        Dictionary<string, string>? initCapabilities = null,
        Dictionary<string, string>? reloadCapabilities = null,
        FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy reloadStrategy =
            FunctionEnvironmentReloadResponse.Types.CapabilitiesUpdateStrategy.Merge,
        Action<FunctionEnvironmentReloadRequest>? onEnvReload = null)
    {
        // Respond to WorkerInitRequest
        var initRequest = await relay._toWorker.Reader.ReadAsync();
        Assert.Equal(StreamingMessage.ContentOneofCase.WorkerInitRequest, initRequest.ContentCase);

        var initResponse = new WorkerInitResponse
        {
            Result = new StatusResult { Status = initStatus }
        };

        if (initCapabilities is not null)
        {
            foreach (var kvp in initCapabilities)
            {
                initResponse.Capabilities[kvp.Key] = kvp.Value;
            }
        }

        relay._pendingWorkerResponse!.TrySetResult(new StreamingMessage { WorkerInitResponse = initResponse });

        if (initStatus != StatusResult.Types.Status.Success)
        {
            return;
        }

        // Respond to FunctionEnvironmentReloadRequest
        var reloadRequest = await relay._toWorker.Reader.ReadAsync();
        Assert.Equal(StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest, reloadRequest.ContentCase);
        onEnvReload?.Invoke(reloadRequest.FunctionEnvironmentReloadRequest);

        // Wait briefly for _pendingWorkerResponse to be set by the next SendAndWaitAsync call.
        await Task.Delay(50);

        var envReloadResponse = new FunctionEnvironmentReloadResponse
        {
            Result = new StatusResult { Status = envReloadStatus },
            CapabilitiesUpdateStrategy = reloadStrategy
        };

        if (reloadCapabilities is not null)
        {
            foreach (var kvp in reloadCapabilities)
            {
                envReloadResponse.Capabilities[kvp.Key] = kvp.Value;
            }
        }

        relay._pendingWorkerResponse!.TrySetResult(new StreamingMessage { FunctionEnvironmentReloadResponse = envReloadResponse });

        if (envReloadStatus != StatusResult.Types.Status.Success)
        {
            return;
        }

        // Respond to FunctionsMetadataRequest
        var metadataRequest = await relay._toWorker.Reader.ReadAsync();
        Assert.Equal(StreamingMessage.ContentOneofCase.FunctionsMetadataRequest, metadataRequest.ContentCase);

        await Task.Delay(50);

        relay._pendingWorkerResponse!.TrySetResult(new StreamingMessage
        {
            FunctionMetadataResponse = new FunctionMetadataResponse
            {
                Result = new StatusResult { Status = StatusResult.Types.Status.Success }
            }
        });
    }

    private static async Task<Exception?> CaptureOutcomeAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
