// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Hosted service that manages outbound gRPC connections to external workers.
/// Implements <see cref="IWorkerConnectionManager"/> for API-driven worker allocation
/// and supports config-driven startup connections.
/// </summary>
internal sealed class WorkerConnectionService : IHostedService, IWorkerConnectionManager, IAsyncDisposable
{
    private static readonly TimeSpan InitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMinutes(1);
    private static readonly int GrpcConnectMaxRetries = 3;
    private static readonly TimeSpan GrpcConnectRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IConnectedWorkerChannelFactory _channelFactory;
    private readonly IConnectedWorkerChannelManager _channelManager;
    private readonly IScriptEventManager _eventManager;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IOutboundGrpcClientFactory _clientFactory;
    private readonly ExternalWorkerOptions _options;
    private readonly HostJsonContentProvider _hostJsonContentProvider;
    private readonly IRuntimeStateManager _runtimeStateManager;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, WorkerConnection> _workers = new();
    private readonly ReaderWriterLockSlim _lifecycleLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private TaskCompletionSource _scriptHostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _firstWorkerClaimed;
    private volatile bool _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerConnectionService"/> class.
    /// </summary>
    public WorkerConnectionService(
        IConnectedWorkerChannelFactory channelFactory,
        IConnectedWorkerChannelManager channelManager,
        IScriptEventManager eventManager,
        IScriptHostManager scriptHostManager,
        IOutboundGrpcClientFactory clientFactory,
        IOptions<ExternalWorkerOptions> options,
        HostJsonContentProvider hostJsonContentProvider,
        IRuntimeStateManager runtimeStateManager,
        ILoggerFactory loggerFactory)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _hostJsonContentProvider = hostJsonContentProvider ?? throw new ArgumentNullException(nameof(hostJsonContentProvider));
        _runtimeStateManager = runtimeStateManager ?? throw new ArgumentNullException(nameof(runtimeStateManager));
        _logger = loggerFactory.CreateLogger<WorkerConnectionService>();
    }

    /// <inheritdoc/>
    public int ActiveWorkerCount => _workers.Values.Count(w => w.Info.State == WorkerConnectionState.Connected);

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogDebug("External worker connections are not enabled.");
            return;
        }

        // Config-driven path: connect to a pre-configured endpoint on startup.
        if (_options.GrpcEndpoint is not null)
        {
            string workerId = $"w_{Guid.NewGuid():N}"[..10];
            await ConnectWorkerAsync(workerId, new Uri(_options.GrpcEndpoint), cancellationToken);
        }
        else
        {
            // API-driven path: no worker is connected at startup.
            // The ScriptHost will block at WaitForContent / WaitForChannelAsync
            // until a worker is linked via PUT /admin/workers/{workerId}.
            // The WebHost layer (admin APIs) remains responsive during this time.
            _logger.LogInformation("No gRPC endpoint configured. Host will wait for worker assignment via admin API.");
        }
    }

    /// <inheritdoc/>
    public async Task ConnectWorkerAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
    {
        await ConnectWorkerAsync(workerId, endpoint, workerHttpEndpoint: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ConnectWorkerAsync(string workerId, Uri endpoint, Uri workerHttpEndpoint, CancellationToken cancellationToken)
    {
        var connectStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("RuntimeWorkerConnect started. WorkerId: {workerId}, Endpoint: {endpoint}, WorkerHttpEndpoint: {workerHttpEndpoint}.", workerId, endpoint, workerHttpEndpoint);

        if (_stopping)
        {
            throw new InvalidOperationException("Cannot connect new workers while the runtime is stopping.");
        }

        var info = new WorkerConnectionInfo
        {
            WorkerId = workerId,
            State = WorkerConnectionState.Connecting
        };

        var worker = new WorkerConnection { Info = info };

        // Hold the read lock only for the _stopping re-check and TryAdd.
        // This guarantees the worker is visible in _workers before
        // DrainAndDisconnectAllAsync can snapshot the dictionary.
        // IMPORTANT: No await allowed inside this lock — ReaderWriterLockSlim is thread-affine.
        _lifecycleLock.EnterReadLock();

        try
        {
            if (_stopping)
            {
                throw new InvalidOperationException("Cannot connect new workers while the runtime is stopping.");
            }

            if (!_workers.TryAdd(workerId, worker))
            {
                throw new InvalidOperationException(
                    $"Worker '{workerId}' is already linked.");
            }

            // The worker is now linked for the purposes of RuntimeState accounting.
            // It remains linked through any state transitions (Connecting, Connected,
            // Draining, Error) until DisconnectWorkerAsync removes it from _workers.
            _runtimeStateManager.OnWorkerLinked(workerId);
            _logger.LogInformation("RuntimeWorkerConnect worker registered. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);
        }
        finally
        {
            _lifecycleLock.ExitReadLock();
        }

        // Start the full connect pipeline (Phase 1 + Phase 2) in the background.
        // We await only InitCompleted (Phase 1: init handshake) so the API can
        // return 200 after the worker is linked. Phase 2 (ScriptHost startup)
        // continues in the background. ConnectCompleted signals after the full
        // pipeline finishes, which DisconnectWorkerAsync awaits before cleanup.
        _ = ConnectWorkerCoreAsync(workerId, endpoint, workerHttpEndpoint, worker, cancellationToken);
        await worker.InitCompleted.Task;
        _logger.LogInformation("RuntimeWorkerConnect init phase completed. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds);
    }

    private async Task ConnectWorkerCoreAsync(string workerId, Uri endpoint, Uri workerHttpEndpoint, WorkerConnection worker, CancellationToken cancellationToken)
    {
        var info = worker.Info;
        var pipelineStart = Stopwatch.GetTimestamp();

        try
        {
            var channel = await EstablishChannelAsync(workerId, endpoint, workerHttpEndpoint, worker, cancellationToken);

            // Phase 1 complete — signal InitCompleted so ConnectWorkerAsync
            // (and the API caller) can return 200.
            worker.InitCompleted.TrySetResult();
            _logger.LogInformation("RuntimeWorkerConnect phase 1 completed. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(pipelineStart).TotalMilliseconds);

            _logger.LogInformation("Worker '{workerId}' linked. Starting host setup.", workerId);

            // Phase 2: ScriptHost startup and capacity advertisement.
            // HTTP requests that arrive before Phase 2 completes will buffer
            // in HostAvailabilityCheckMiddleware.DelayUntilHostReadyAsync().
            //
            // We await Phase 2 here (not fire-and-forget) so that
            // ConnectCompleted signals only after the full pipeline finishes.
            // This ensures DisconnectWorkerAsync won't tear down resources
            // while Phase 2 is still running.
            await CompleteWorkerSetupAsync(workerId, worker, channel, _shutdownCts.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to connect worker '{workerId}'.", workerId);

            info.State = WorkerConnectionState.Error;
            info.ErrorMessage = ex.Message;

            // If Phase 1 failed, fault InitCompleted so the API caller gets the exception.
            // If Phase 1 already succeeded, TrySetException is a no-op.
            worker.InitCompleted.TrySetException(ex);

            try
            {
                await CleanupWorkerResourcesAsync(workerId, worker);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Cleanup after failed connect threw for worker '{workerId}'.", workerId);
            }

            _workers.TryRemove(workerId, out _);
            _runtimeStateManager.OnWorkerUnlinked(workerId);
        }
        finally
        {
            // Signal that the full pipeline (Phase 1 + Phase 2) has finished.
            // DisconnectWorkerAsync awaits this before cleaning up.
            worker.ConnectCompleted.TrySetResult();
            _logger.LogInformation("RuntimeWorkerConnect pipeline completed. WorkerId: {workerId}, State: {state}, TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", workerId, info.State, Stopwatch.GetElapsedTime(pipelineStart).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Phase 1: Establishes the gRPC connection, performs the init handshake
    /// (WorkerInitRequest/WorkerInitResponse), extracts host.json from
    /// capabilities, and registers the channel. Returns after the worker
    /// is linked and the init handshake has succeeded.
    /// </summary>
    private async Task<IConnectedWorkerChannel> EstablishChannelAsync(string workerId, Uri endpoint, Uri workerHttpEndpoint, WorkerConnection worker, CancellationToken cancellationToken)
    {
        var phaseStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("RuntimeWorkerConnect phase 1 started. WorkerId: {workerId}, Endpoint: {endpoint}, WorkerHttpEndpoint: {workerHttpEndpoint}.", workerId, endpoint, workerHttpEndpoint);

        _eventManager.AddGrpcChannels(workerId);
        _logger.LogInformation("RuntimeWorkerConnect gRPC channels registered. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        var client = _clientFactory.Create();
        worker.Client = client;

        // Retry gRPC connect with backoff. The SWIFT port mapping for the worker
        // pod may not be routable immediately after pod creation, causing the TCP
        // connect to hang until ConnectTimeout fires. Retrying gives SWIFT time
        // to propagate while keeping the overall budget within the init timeout.
        for (int attempt = 1; ; attempt++)
        {
            var attemptStart = Stopwatch.GetTimestamp();
            _logger.LogInformation("RuntimeWorkerConnect gRPC connect attempt started. WorkerId: {workerId}, Attempt: {attempt}, MaxRetries: {maxRetries}, Endpoint: {endpoint}.", workerId, attempt, GrpcConnectMaxRetries, endpoint);

            try
            {
                await client.ConnectAsync(workerId, endpoint, cancellationToken);
                _logger.LogInformation("RuntimeWorkerConnect gRPC connect attempt completed. WorkerId: {workerId}, Attempt: {attempt}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, attempt, Stopwatch.GetElapsedTime(attemptStart).TotalMilliseconds);
                break;
            }
            catch (Exception ex) when (attempt < GrpcConnectMaxRetries && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "RuntimeWorkerConnect gRPC connect attempt failed. WorkerId: {workerId}, Attempt: {attempt}, MaxRetries: {maxRetries}, Endpoint: {endpoint}, AttemptElapsedMilliseconds: {attemptElapsedMilliseconds}, RetryDelaySeconds: {retryDelaySeconds}.",
                    workerId, attempt, GrpcConnectMaxRetries, endpoint, Stopwatch.GetElapsedTime(attemptStart).TotalMilliseconds, GrpcConnectRetryDelay.TotalSeconds);

                // Dispose the failed client and create a fresh one for the next attempt.
                await client.DisposeAsync();
                client = _clientFactory.Create();
                worker.Client = client;

                await Task.Delay(GrpcConnectRetryDelay, cancellationToken);
            }
        }

        _logger.LogInformation("RuntimeWorkerConnect gRPC stream established. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        var workerConfig = new RpcWorkerConfig
        {
            Description = new RpcWorkerDescription
            {
                Language = "external",
                WorkerDirectory = string.Empty
            },
            CountOptions = new WorkerProcessCountOptions()
        };

        var channel = _channelFactory.Create(workerId, workerConfig);
        _logger.LogInformation("RuntimeWorkerConnect channel created. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        var channelStart = Stopwatch.GetTimestamp();
        await channel.StartWorkerProcessAsync(cancellationToken);
        _logger.LogInformation("RuntimeWorkerConnect channel inbound processing started. WorkerId: {workerId}, StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(channelStart).TotalMilliseconds, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        var initStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("RuntimeWorkerConnect init handshake wait started. WorkerId: {workerId}, TimeoutSeconds: {timeoutSeconds}.", workerId, InitTimeout.TotalSeconds);
        await channel.WaitForInitAsync(InitTimeout, cancellationToken);
        _logger.LogInformation("RuntimeWorkerConnect init handshake completed. WorkerId: {workerId}, StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(initStart).TotalMilliseconds, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        // Extract host.json from worker capabilities.
        string hostJson = channel.GetCapabilityState("host_configuration_json");
        if (hostJson is not null)
        {
            _logger.LogDebug("Received host.json configuration from worker '{workerId}'.", workerId);
            _hostJsonContentProvider.SetContent(hostJson);
        }
        else
        {
            _logger.LogWarning("Worker '{workerId}' did not provide host_configuration_json capability.", workerId);
        }

        if (workerHttpEndpoint is not null)
        {
            channel.OverrideHttpProxyEndpoint(workerHttpEndpoint);
            _logger.LogInformation("Worker '{workerId}' HTTP proxy endpoint set to platform endpoint {endpoint}.", workerId, workerHttpEndpoint);
        }

        // Register the channel directly — no event needed.
        _channelManager.AddChannel(workerId, channel);
        _logger.LogInformation("RuntimeWorkerConnect channel registered. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        // Subscribe to drain signals from the worker proxy.
        channel.DrainRequested += OnWorkerDrainRequested;

        _logger.LogInformation("RuntimeWorkerConnect phase 1 finished. WorkerId: {workerId}, TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        return channel;
    }

    /// <summary>
    /// Phase 2: Starts the ScriptHost (first worker) or sets up the channel
    /// for dispatch (subsequent workers), then advertises capacity. Runs in
    /// the background after the API returns 200.
    /// </summary>
    private async Task CompleteWorkerSetupAsync(string workerId, WorkerConnection worker, IConnectedWorkerChannel channel, CancellationToken cancellationToken)
    {
        var setupStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("RuntimeWorkerSetup started. WorkerId: {workerId}.", workerId);

        // Start or update the ScriptHost based on whether this is the first worker.
        // The first caller either starts or waits for the ScriptHost; concurrent
        // callers block until startup completes, then call SetupChannel.
        if (Interlocked.CompareExchange(ref _firstWorkerClaimed, 1, 0) == 0)
        {
            // First worker: start the ScriptHost. In external worker mode,
            // WebJobsScriptHostService is not registered as an IHostedService,
            // so the ScriptHost hasn't started yet. Now that a worker has delivered
            // host.json and registered a channel, WaitForContent and WaitForChannelAsync
            // will return immediately when the ScriptHost builds.
            var tcs = _scriptHostStarted;
            try
            {
                var scriptHostStart = Stopwatch.GetTimestamp();
                _logger.LogInformation("RuntimeWorkerSetup starting ScriptHost for first worker. WorkerId: {workerId}.", workerId);
                await _scriptHostManager.StartAsync(cancellationToken);
                _logger.LogInformation("RuntimeWorkerSetup ScriptHost started. WorkerId: {workerId}, StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(scriptHostStart).TotalMilliseconds, Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                // Fault the current TCS so any concurrent waiters get the exception
                // (they'll propagate it and the platform can retry those workers too).
                // Replace the TCS BEFORE releasing the gate so the next winner sees
                // a fresh TCS, not the faulted one.
                tcs.TrySetException(ex);
                _scriptHostStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Exchange(ref _firstWorkerClaimed, 0);
                throw;
            }
        }
        else
        {
            // Wait for the first worker's StartAsync to complete before resolving the dispatcher.
            var waitStart = Stopwatch.GetTimestamp();
            _logger.LogInformation("RuntimeWorkerSetup waiting for ScriptHost before adding subsequent worker. WorkerId: {workerId}.", workerId);
            await _scriptHostStarted.Task.WaitAsync(cancellationToken);
            _logger.LogInformation("RuntimeWorkerSetup ScriptHost wait completed. WorkerId: {workerId}, StepElapsedMilliseconds: {stepElapsedMilliseconds}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds, Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds);

            if (Utility.TryGetHostService(_scriptHostManager, out ConnectedWorkerInvocationDispatcher dispatcher))
            {
                dispatcher.SetupChannel(channel);
                _logger.LogInformation("RuntimeWorkerSetup channel setup completed for subsequent worker. WorkerId: {workerId}, ElapsedMilliseconds: {elapsedMilliseconds}.", workerId, Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds);
            }
        }

        // [CS-TODO] Replace with value reported by the worker during the init
        // handshake (expected as a "max_concurrency" capability on
        // WorkerInitResponse, parsed via channel.GetCapabilityState(...) like
        // "host_configuration_json" above). Until then, hard-code per the App
        // Server contract.
        const int workerSlotCapacity = 16;
        _runtimeStateManager.OnWorkerCapacityAvailable(workerId, workerSlotCapacity);

        worker.Info.State = WorkerConnectionState.Connected;
        _logger.LogInformation("RuntimeWorkerSetup completed. WorkerId: {workerId}, WorkerSlotCapacity: {workerSlotCapacity}, TotalElapsedMilliseconds: {totalElapsedMilliseconds}.", workerId, workerSlotCapacity, Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds);
    }

    /// <inheritdoc/>
    public async Task DisconnectWorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disconnecting worker '{workerId}'.", workerId);

        if (!_workers.TryGetValue(workerId, out var worker))
        {
            return;
        }

        // Atomically claim the disconnect — prevents double-disconnect races.
        if (!worker.TryClaimDisconnect())
        {
            return;
        }

        // Wait for any in-flight ConnectWorkerAsync to finish before cleaning up.
        await worker.ConnectCompleted.Task;

        // Withdraw this worker's capacity from the shared slot pool up front,
        // before we start the (potentially long) drain. From this point on
        // the worker won't serve new invocations, so its capacity must not be
        // advertised to the App Server. The worker stays linked (visible in
        // LinkedWorkerCount) until it is removed from _workers below.
        // OnWorkerCapacityUnavailable is idempotent and a no-op if the worker
        // never contributed capacity (e.g. connect failed before handshake).
        _runtimeStateManager.OnWorkerCapacityUnavailable(workerId);

        // Mark channel as draining so no new invocations are routed.
        var channel = _channelManager.GetChannel(workerId);
        if (channel is IConnectedWorkerChannel connectedChannel)
        {
            connectedChannel.BeginDrain();

            // Notify the worker proxy it should enter the Draining state.
            // In worker-initiated drain the proxy already knows,
            // but sending this is idempotent and keeps the code path simple.
            connectedChannel.SendWorkerDrainRequest();
        }

        worker.Info.State = WorkerConnectionState.Draining;
        _logger.LogInformation("Worker '{workerId}' marked as draining. Waiting for in-flight invocations.", workerId);

        try
        {
            // Drain in-flight invocations with timeout before cleanup.
            if (channel is not null)
            {
                try
                {
                    await channel.DrainInvocationsAsync()
                        .WaitAsync(DrainTimeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Drain timeout exceeded for worker '{workerId}'. Proceeding with disconnect.", workerId);
                }

                // Send WorkerDrainComplete to the worker proxy before closing the connection.
                if (channel is IConnectedWorkerChannel drainedChannel)
                {
                    drainedChannel.SendWorkerDrainComplete();
                    _logger.LogInformation("Sent WorkerDrainComplete to worker '{workerId}'.", workerId);
                }
            }

            // [CS-TODO] When the last worker is drained (scale-in), should the runtime
            // pause trigger listeners or enter a degraded state? For now we just disconnect.

            _logger.LogInformation("Worker '{workerId}' disconnected.", workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting worker '{workerId}'.", workerId);

            worker.Info.State = WorkerConnectionState.Error;
            worker.Info.ErrorMessage = ex.Message;

            throw;
        }
        finally
        {
            // Always release resources and drop the worker from tracking, even
            // when drain throws. Slot-pool capacity was already returned at the
            // start of the drain; here we reverse the OnWorkerLinked accounting
            // so a failed disconnect can't permanently inflate LinkedWorkerCount
            // and eventually block the platform from linking new workers.
            try
            {
                await CleanupWorkerResourcesAsync(workerId, worker);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Cleanup after disconnect threw for worker '{workerId}'.", workerId);
            }

            _workers.TryRemove(workerId, out _);
            _runtimeStateManager.OnWorkerUnlinked(workerId);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkerConnectionInfo> GetWorkerStatuses()
        => _workers.Values.Select(w => w.Info).ToList().AsReadOnly();

    /// <inheritdoc/>
    public WorkerConnectionInfo GetWorkerStatus(string workerId)
        => _workers.TryGetValue(workerId, out var worker) ? worker.Info : null;

    /// <summary>
    /// Waits for the full connect pipeline (Phase 1 + Phase 2) to complete for the
    /// specified worker. Returns immediately if the worker is not tracked or has
    /// already completed. Intended for test use only.
    /// </summary>
    internal Task WaitForWorkerConnectAsync(string workerId)
        => _workers.TryGetValue(workerId, out var worker) ? worker.ConnectCompleted.Task : Task.CompletedTask;

    /// <inheritdoc/>
    public async Task DrainAndDisconnectAllAsync(CancellationToken cancellationToken)
    {
        IList<string> workerIds;

        // Hold the write lock to atomically set _stopping and snapshot _workers.
        // This guarantees no in-flight ConnectWorkerAsync can TryAdd between
        // setting the flag and taking the snapshot.
        // IMPORTANT: No await allowed inside this lock — ReaderWriterLockSlim is thread-affine.
        _lifecycleLock.EnterWriteLock();

        try
        {
            _stopping = true;
            _shutdownCts.Cancel();
            workerIds = _workers.Keys.ToList();
        }
        finally
        {
            _lifecycleLock.ExitWriteLock();
        }

        // Tell the runtime-state manager immediately so GetState/AcquireSlots
        // report zero slots for the entire drain window, rather than leaking
        // capacity per-worker as each disconnect completes.
        _runtimeStateManager.SetStopping();

        if (workerIds.Count == 0)
        {
            _logger.LogInformation("No workers connected. Nothing to drain.");
            return;
        }

        _logger.LogInformation("Draining and disconnecting {count} worker(s).", workerIds.Count);

        var tasks = workerIds.Select(id => DisconnectWorkerAsync(id, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.LogInformation("All workers drained and disconnected.");
    }

    /// <summary>
    /// Event handler for <see cref="IConnectedWorkerChannel.DrainRequested"/>.
    /// Fired when the worker proxy sends a <c>WorkerDrainRequest</c> over gRPC.
    /// </summary>
    private void OnWorkerDrainRequested(string workerId)
    {
        _logger.LogInformation("Received WorkerDrainRequest for worker '{workerId}'.", workerId);

        // Fire-and-forget — disconnect (which includes drain) runs in the background.
        _ = DisconnectWorkerAsync(workerId, CancellationToken.None).ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Error draining worker '{workerId}'.", workerId);
                }
            },
            TaskScheduler.Default);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Reuse the same drain path as /admin/instance/stop so workers get
        // the full graceful shutdown (BeginDrain → DrainInvocations →
        // SendWorkerDrainComplete) and the _stopping flag + lifecycle lock
        // prevent races with in-flight connects.
        try
        {
            await DrainAndDisconnectAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during graceful shutdown of workers.");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _workers)
        {
            if (kvp.Value.Client is not null)
            {
                try
                {
                    await kvp.Value.Client.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing gRPC client for worker '{workerId}'.", kvp.Key);
                }
            }
        }

        _workers.Clear();
        _lifecycleLock.Dispose();
        _shutdownCts.Dispose();
    }

    /// <summary>
    /// Disposes the gRPC client, removes gRPC event channels, and drains the
    /// worker channel. Does NOT remove the worker from <see cref="_workers"/>
    /// so the Error state remains visible to callers.
    /// </summary>
    private async Task CleanupWorkerResourcesAsync(string workerId, WorkerConnection worker)
    {
        // Unsubscribe from drain events before disposing the channel.
        var channel = _channelManager.GetChannel(workerId);
        if (channel is IConnectedWorkerChannel connectedChannel)
        {
            connectedChannel.DrainRequested -= OnWorkerDrainRequested;
        }

        await _channelManager.ShutdownChannelAsync(workerId);

        if (worker.Client is not null)
        {
            try
            {
                await worker.Client.DisposeAsync();
            }
            catch (ObjectDisposedException)
            {
                // Client may have already been partially disposed during a failed connect.
            }

            worker.Client = null;
        }

        _eventManager.RemoveGrpcChannels(workerId);
    }

    /// <summary>
    /// Internal tracking type that pairs a worker's API-visible state
    /// with its gRPC client resource.
    /// </summary>
    private class WorkerConnection
    {
        private int _disconnecting;

        public WorkerConnectionInfo Info { get; set; }

        public IOutboundGrpcClient Client { get; set; }

        /// <summary>
        /// Gets a signal that completes when Phase 1 (gRPC connect + init handshake +
        /// channel registration) has finished (success or failure). The outer
        /// <c>ConnectWorkerAsync</c> awaits this so the API can return 200 after the
        /// init handshake while Phase 2 continues in the background.
        /// </summary>
        public TaskCompletionSource InitCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets a signal that completes when the full connect pipeline (Phase 1 +
        /// Phase 2) has finished. The outer <c>DisconnectWorkerAsync</c> awaits this
        /// before cleaning up to avoid racing with an in-flight connection or
        /// background setup.
        /// </summary>
        public TaskCompletionSource ConnectCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Atomically claims the disconnect operation. Returns <see langword="true"/> if this
        /// caller is the first to claim it; subsequent callers get <see langword="false"/>.
        /// </summary>
        public bool TryClaimDisconnect() => Interlocked.CompareExchange(ref _disconnecting, 1, 0) == 0;
    }
}
