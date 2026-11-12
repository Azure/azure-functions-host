// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class RpcClientScriptHostStartupIntegrationTests
{
    private static async Task VerifyTriggerListenerLifecycleAsync(
        string functionName, string bindingJson, Action<IWebJobsBuilder> configureWebJobs,
        IReadOnlyDictionary<string, string> configuration, Func<string, CancellationToken, Task>? publishAsync,
        Func<string, CancellationToken, Task>? assertPendingAsync = null, string hostConfiguration = """{"version":"2.0"}""",
        TimeSpan? quietWindow = null, string? functionGroup = null, string? hostId = null,
        IReadOnlyDictionary<string, string>? additionalFunctions = null, bool startOnly = false)
    {
        int cycles = ReadLifecycleSetting("BYOC_TEST_LIFECYCLE_CYCLES", 1);
        TimeSpan quietPeriod = quietWindow ?? TimeSpan.FromSeconds(ReadLifecycleSetting("BYOC_TEST_LIFECYCLE_QUIET_SECONDS", 2));
        if (quietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietWindow));
        }
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3 * cycles) + (quietPeriod * (2 * cycles)));
        CancellationToken token = timeout.Token;
        Stopwatch elapsed = Stopwatch.StartNew();
        string runId = $"{functionName}-{Guid.NewGuid():N}";
        string? logDirectory = Environment.GetEnvironmentVariable("BYOC_TEST_LIFECYCLE_LOG_DIRECTORY");
        string? logPath = null;
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(logDirectory, runId);
        }

        using StreamWriter? phaseLog = logPath is null ? null : new StreamWriter($"{logPath}.phases.log") { AutoFlush = true };
        void Record(string message)
        {
            string entry = $"{DateTimeOffset.UtcNow:O} +{elapsed.Elapsed.TotalSeconds:F3}s {message}";
            phaseLog?.WriteLine(entry);
            Console.WriteLine(entry);
        }

        static RpcFunctionMetadata CreateMetadata(string name, string binding) => new()
        {
            FunctionId = name,
            Name = name,
            Language = "dotnet-isolated",
            ScriptFile = "Sample.dll",
            EntryPoint = $"Sample.{name}.Run",
            Status = new() { Status = StatusResult.Types.Status.Success },
            RawBindings = { binding },
        };
        List<RpcFunctionMetadata> metadata = [CreateMetadata(functionName, bindingJson)];
        if (additionalFunctions is not null)
        {
            metadata.AddRange(additionalFunctions.Select(function => CreateMetadata(function.Key, function.Value)));
        }

        string group = functionGroup ?? $"function:{functionName}";
        ConcurrentQueue<(string WorkerId, InvocationRequest Request)> requests = new();
        await using TestWorker indexer = await TestWorker.StartAsync("indexer", functionGroupName: "http", metadata: metadata);
        await using ClientTestHost testHost = new(services =>
        {
            services.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(new TriggerWebJobsConfiguration(configureWebJobs));
            services.AddSingleton<IConfigureBuilder<IConfigurationBuilder>>(new TriggerHostConfiguration(configuration));
            services.AddSingleton<IConfigureBuilder<ILoggingBuilder>>(new TriggerListenerLogging());
        }, hostConfiguration: hostConfiguration);
        foreach ((string key, string value) in configuration)
        {
            testHost.Configuration[key] = value;
        }

        try
        {
            hostId ??= $"lifecycle-{Guid.NewGuid():N}"[..32];
            testHost.Configuration["AzureFunctionsWebHost:hostId"] = hostId;
            Record($"RUN BEGIN cycles={cycles} quietSeconds={quietPeriod.TotalSeconds} hostId={hostId}");
            await testHost.Host.StartAsync(token);
            IWorkerChannelRegistry registry = testHost.Host.Services.GetRequiredService<IWorkerChannelRegistry>();
            IScriptHostManager manager = testHost.Host.Services.GetRequiredService<IScriptHostManager>();
            RpcClientScriptHostStartupCoordinator coordinator = testHost.Host.Services.GetRequiredService<RpcClientScriptHostStartupCoordinator>();
            await registry.LinkAsync("indexer", indexer.Endpoint, cancellationToken: token);
            await coordinator.ActivationTask!.WaitAsync(token);
            await WaitForHostStateAsync(manager, ScriptHostState.Running, token);
            ScriptHost scriptHost = manager.Services!.GetRequiredService<ScriptHost>();
            Assert.Empty(scriptHost.FunctionErrors);
            Assert.Equal(metadata.Count, scriptHost.Functions.Count);
            Assert.All(scriptHost.Functions, function => Assert.Equal(group, function.Metadata.GetFunctionGroup()));
            Record("Host running; wrong-group indexing worker linked.");

            int BoundaryCount(string name, string listenerFunction) => startOnly
                ? testHost.Logs.Count(log => string.Equals(log.EventId.Name,
                    name is "StartingListener" or "StartedListener" ? "BlobQueueResumed" : "BlobQueuePaused", StringComparison.Ordinal))
                : testHost.Logs.Count(log => string.Equals(log.EventId.Name, name, StringComparison.Ordinal) &&
                    log.Message is not null && log.Message.Contains($"'{listenerFunction}'", StringComparison.Ordinal));
            bool BoundaryReached(string name, int count) => metadata.All(function => BoundaryCount(name, function.Name) == count);
            bool HasSucceeded(InvocationRequest request) => testHost.Logs.Any(log =>
                string.Equals(log.EventId.Name, "FunctionCompleted", StringComparison.Ordinal) &&
                log.Message is not null && log.Message.Contains(request.InvocationId, StringComparison.Ordinal) &&
                log.Message.Contains("Succeeded", StringComparison.Ordinal));
            int CompletionCount() => testHost.Logs.Count(log => string.Equals(log.EventId.Name, "FunctionCompleted", StringComparison.Ordinal));
            void ThrowForLifecycleFailure()
            {
                var failure = testHost.Logs.FirstOrDefault(log =>
                    string.Equals(log.EventId.Name, "ListenerStartFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "ListenerStopFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "ListenerDisposeFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "ListenerDisposeBlocked", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "ListenerRestartBlocked", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "CancellationCallbackFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "ChannelFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "ChannelDisposalFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "DrainFailed", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "DrainAlreadyDetached", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "OnListenerStartError", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "OnTriggerListenerStopError", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "FunctionNotFound", StringComparison.Ordinal) ||
                    string.Equals(log.EventId.Name, "BlobNotFound", StringComparison.Ordinal));
                if (failure.EventId.Name is not null)
                {
                    throw new InvalidOperationException($"{failure.EventId.Name}: {failure.Message}", failure.Exception);
                }
            }

            async Task WaitForLifecycleConditionAsync(Func<bool> condition, CancellationToken cancellationToken)
            {
                while (true)
                {
                    ThrowForLifecycleFailure();
                    if (condition())
                    {
                        return;
                    }

                    await Task.Delay(20, cancellationToken);
                }
            }

            async Task VerifyQuietAsync(int cycle, string phase, string marker, CancellationToken cancellationToken)
            {
                int starts = phase is "PRELINK" ? (cycle - 1) * 2 : (cycle * 2) - 1;
                Assert.True(BoundaryReached("StartedListener", starts));
                Assert.True(BoundaryReached("StoppedListener", starts));
                int completions = CompletionCount();
                int invocations = requests.Count;
                if (publishAsync is not null)
                {
                    await publishAsync(marker, cancellationToken);
                }

                Record($"CYCLE {cycle}/{cycles} {phase} BEGIN marker={marker} starts={starts} completions={completions}");
                Stopwatch quiet = Stopwatch.StartNew();
                while (quiet.Elapsed < quietPeriod)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                    ThrowForLifecycleFailure();
                    Assert.True(BoundaryReached("StartedListener", starts));
                    Assert.True(BoundaryReached("StoppedListener", starts));
                    Assert.Equal(completions, CompletionCount());
                    Assert.Equal(invocations, requests.Count);
                    Assert.Equal(0, indexer.Service.Invocations);
                }

                if (assertPendingAsync is not null)
                {
                    await assertPendingAsync(marker, cancellationToken);
                }

                Record($"CYCLE {cycle}/{cycles} {phase} PASS quietSeconds={quiet.Elapsed.TotalSeconds:F3} workPending={publishAsync is not null}");
            }

            for (int cycle = 1; cycle <= cycles; cycle++)
            {
                Stopwatch cycleElapsed = Stopwatch.StartNew();
                using CancellationTokenSource cycleTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                cycleTimeout.CancelAfter(TimeSpan.FromMinutes(3) + (quietPeriod * 2));
                CancellationToken cycleToken = cycleTimeout.Token;
                string markerPrefix = $"{runId}-cycle-{cycle}";
                string firstId = $"first-{cycle}-{Guid.NewGuid():N}";
                string secondId = $"second-{cycle}-{Guid.NewGuid():N}";
                TaskCompletionSource<string> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                string heldMarker = string.Empty;
                string heldWorker = string.Empty;
                int holdNext = 0;
                async Task OnInvocationAsync(string workerId, InvocationRequest request, CancellationToken cancellationToken)
                {
                    requests.Enqueue((workerId, request));
                    if (Interlocked.CompareExchange(ref holdNext, 0, 0) != 0 && string.Equals(workerId, heldWorker, StringComparison.Ordinal) &&
                        (publishAsync is null || HasMarker(request, heldMarker)) && Interlocked.Exchange(ref holdNext, 0) != 0)
                    {
                        held.TrySetResult(request.InvocationId);
                        await release.Task.WaitAsync(cancellationToken);
                    }
                }

                await using TestWorker first = await TestWorker.StartAsync(firstId, functionGroupName: group, metadata: metadata,
                    onInvocation: (request, cancellationToken) => OnInvocationAsync(firstId, request, cancellationToken));
                await using TestWorker second = await TestWorker.StartAsync(secondId, functionGroupName: group, metadata: metadata,
                    onInvocation: (request, cancellationToken) => OnInvocationAsync(secondId, request, cancellationToken));

                int SuccessfulCount(string workerId) => requests.Count(item =>
                    string.Equals(item.WorkerId, workerId, StringComparison.Ordinal) && HasSucceeded(item.Request));
                bool MarkerSucceeded(string workerId, string marker) => metadata.All(function => requests.Any(item =>
                    string.Equals(item.WorkerId, workerId, StringComparison.Ordinal) &&
                    string.Equals(item.Request.FunctionId, function.FunctionId, StringComparison.Ordinal) &&
                    HasMarker(item.Request, marker) && HasSucceeded(item.Request)));

                async Task RetireAsync(TestWorker worker, string workerId, int generation, string marker)
                {
                    held = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    heldMarker = marker;
                    heldWorker = workerId;
                    Interlocked.Exchange(ref holdNext, 1);
                    using CancellationTokenRegistration releaseOnTimeout = cycleToken.Register(() => release.TrySetResult());
                    if (publishAsync is not null)
                    {
                        await publishAsync(marker, cycleToken);
                    }

                    await WaitForLifecycleConditionAsync(() => held.Task.IsCompleted, cycleToken);
                    string invocationId = await held.Task;
                    Record($"CYCLE {cycle}/{cycles} HOLD worker={workerId} invocation={invocationId}");
                    try
                    {
                        Assert.Equal(1, await registry.ScaleInWorkersAsync(group, false, 0, cycleToken));
                        await WaitForLifecycleConditionAsync(() => BoundaryReached("StoppingListener", generation), cycleToken);
                        Assert.True(registry.TryGetInitializedChannel(workerId, out _));
                        worker.Service.ExpectDisconnect();
                        Record($"CYCLE {cycle}/{cycles} STOPPING generation={generation} workerStillRegistered=true");
                    }
                    finally
                    {
                        release.TrySetResult();
                    }

                    await WaitForLifecycleConditionAsync(() => requests.Any(item =>
                        string.Equals(item.Request.InvocationId, invocationId, StringComparison.Ordinal) && HasSucceeded(item.Request)), cycleToken);
                    await WaitForLifecycleConditionAsync(() => BoundaryReached("StoppedListener", generation), cycleToken);
                    await WaitForLifecycleConditionAsync(() => !registry.TryGetInitializedChannel(workerId, out _), cycleToken);
                    await worker.Service.Disconnected.Task.WaitAsync(cycleToken);
                    Record($"CYCLE {cycle}/{cycles} STOPPED generation={generation} heldInvocationSucceeded={invocationId} workerDisconnected=true");
                }

                Record($"CYCLE {cycle}/{cycles} BEGIN");
                int firstGeneration = (cycle * 2) - 1;
                string delayed = $"{markerPrefix}-delayed";
                await VerifyQuietAsync(cycle, "PRELINK", delayed, cycleToken);
                await registry.LinkAsync(firstId, first.Endpoint, cancellationToken: cycleToken);
                await WaitForLifecycleConditionAsync(() => BoundaryReached("StartedListener", firstGeneration), cycleToken);
                Record($"CYCLE {cycle}/{cycles} STARTED generation={firstGeneration} worker={firstId}");
                if (publishAsync is not null)
                {
                    await WaitForLifecycleConditionAsync(() => MarkerSucceeded(firstId, delayed), cycleToken);
                    await publishAsync($"{markerPrefix}-running", cycleToken);
                    await WaitForLifecycleConditionAsync(() => MarkerSucceeded(firstId, $"{markerPrefix}-running"), cycleToken);
                }

                await WaitForLifecycleConditionAsync(() => SuccessfulCount(firstId) >= 2, cycleToken);
                Record($"CYCLE {cycle}/{cycles} RUNNING PASS successfulInvocations={SuccessfulCount(firstId)}");
                await RetireAsync(first, firstId, firstGeneration, $"{markerPrefix}-held");
                int retiredInvocations = first.Service.Invocations;

                string backlog = $"{markerPrefix}-backlog";
                await VerifyQuietAsync(cycle, "ZERO", backlog, cycleToken);
                await registry.LinkAsync(secondId, second.Endpoint, cancellationToken: cycleToken);
                await WaitForLifecycleConditionAsync(() => BoundaryReached("StartedListener", firstGeneration + 1), cycleToken);
                Record($"CYCLE {cycle}/{cycles} RESTARTED generation={firstGeneration + 1} worker={secondId}");
                if (publishAsync is not null)
                {
                    await WaitForLifecycleConditionAsync(() => MarkerSucceeded(secondId, backlog), cycleToken);
                    await publishAsync($"{markerPrefix}-resumed", cycleToken);
                    await WaitForLifecycleConditionAsync(() => MarkerSucceeded(secondId, $"{markerPrefix}-resumed"), cycleToken);
                }

                await WaitForLifecycleConditionAsync(() => SuccessfulCount(secondId) >= 2, cycleToken);
                Record($"CYCLE {cycle}/{cycles} RESUMED PASS successfulInvocations={SuccessfulCount(secondId)}");
                await RetireAsync(second, secondId, firstGeneration + 1, $"{markerPrefix}-resumed-held");
                Assert.Equal(retiredInvocations, first.Service.Invocations);
                Assert.Equal(0, indexer.Service.Invocations);
                Assert.Equal(requests.Count, requests.Count(item => HasSucceeded(item.Request)));
                Assert.Equal(requests.Count, CompletionCount());
                Assert.Same(scriptHost, manager.Services!.GetRequiredService<ScriptHost>());
                Assert.Equal(ScriptHostState.Running, manager.State);
                Record($"CYCLE {cycle}/{cycles} PASS elapsedSeconds={cycleElapsed.Elapsed.TotalSeconds:F3} " +
                    $"starts={firstGeneration + 1} stops={firstGeneration + 1} completions={CompletionCount()}");
            }

            ThrowForLifecycleFailure();
            if (startOnly)
            {
                foreach (RpcFunctionMetadata function in metadata)
                {
                    Assert.Single(testHost.Logs.Where(log => string.Equals(log.EventId.Name, "StartedListener", StringComparison.Ordinal) &&
                        log.Message is not null && log.Message.Contains($"'{function.Name}'", StringComparison.Ordinal)));
                }

                Assert.DoesNotContain(testHost.Logs, log => log.EventId.Name is "StoppingListener" or "StoppedListener");
                // The shared SDK timer must stop while its command is waiting behind a closed gate.
                await testHost.Host.StopAsync(token);
                ThrowForLifecycleFailure();
            }

            Record($"RUN PASS cycles={cycles} starts={cycles * 2} stops={cycles * 2} completions={CompletionCount()}");
        }
        catch (Exception exception)
        {
            string logs = string.Join(Environment.NewLine, testHost.Logs.Where(log =>
                !string.Equals(log.EventId.Name, "Request", StringComparison.Ordinal) &&
                !string.Equals(log.EventId.Name, "Response", StringComparison.Ordinal)).TakeLast(60).Select(log =>
                $"{log.Level} {log.EventId.Name}: {log.Message} {log.Exception}"));
            string invocationDetails = string.Join(Environment.NewLine, requests.TakeLast(10).Select(item => $"{item.WorkerId}: {item.Request}"));
            Record($"RUN FAIL {exception}");
            throw new InvalidOperationException(
                $"Trigger lifecycle failed for {functionName}. Host state: {testHost.Host.Services.GetRequiredService<IScriptHostManager>().State}.{Environment.NewLine}{logs}{Environment.NewLine}Invocations: {invocationDetails}", exception);
        }
        finally
        {
            if (logPath is not null)
            {
                await File.WriteAllLinesAsync($"{logPath}.host.log", testHost.Logs.Select(log =>
                    $"{log.Level} {log.EventId.Name}: {log.Message} {log.Exception}"));
                await File.WriteAllLinesAsync($"{logPath}.invocations.log", requests.Select(item => $"{item.WorkerId}: {item.Request}"));
            }
        }
    }

    private static bool HasMarker(InvocationRequest request, string marker) => request.InputData.Any(input =>
        input.Data.ToString().Contains(marker, StringComparison.Ordinal) ||
        input.Data.Bytes.ToStringUtf8().Contains(marker, StringComparison.Ordinal));

    private static int ReadLifecycleSetting(string name, int defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        int result = value is null ? defaultValue : int.Parse(value, CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(result, name);

        return result;
    }

    private sealed class TriggerWebJobsConfiguration(Action<IWebJobsBuilder> configure) : IConfigureBuilder<IWebJobsBuilder>
    {
        public void Configure(IWebJobsBuilder builder) => configure(builder);
    }

    private sealed class TriggerListenerLogging : IConfigureBuilder<ILoggingBuilder>
    {
        public void Configure(ILoggingBuilder builder)
        {
            builder.AddFilter("Azure.Functions.Rpc.Client", LogLevel.Debug);
            builder.AddFilter("Microsoft.Azure.WebJobs.Extensions.Storage.Blobs.Listeners.BlobListener", LogLevel.Debug);
        }
    }

    private sealed class TriggerHostConfiguration(IReadOnlyDictionary<string, string> configuration) : IConfigureBuilder<IConfigurationBuilder>
    {
        public void Configure(IConfigurationBuilder builder) => builder.AddInMemoryCollection(
            configuration.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
    }
}

/// <summary>
/// Runs a real trigger test only when all required environment settings have been supplied.
/// </summary>
public sealed class TriggerLifecycleFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TriggerLifecycleFactAttribute"/> class.
    /// Initializes a conditional integration test without suppressing failures from configured backends.
    /// </summary>
    /// <param name="requiredEnvironmentVariables">Environment settings required by the test.</param>
    public TriggerLifecycleFactAttribute(params string[] requiredEnvironmentVariables)
    {
        string[] missing = requiredEnvironmentVariables.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
        if (missing.Length > 0)
        {
            Skip = $"Required lifecycle test settings are absent: {string.Join(", ", missing)}.";
        }
    }
}
