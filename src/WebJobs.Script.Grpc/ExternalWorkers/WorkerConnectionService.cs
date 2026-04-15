// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    private readonly IConnectedWorkerChannelFactory _channelFactory;
    private readonly IConnectedWorkerChannelManager _channelManager;
    private readonly IScriptEventManager _eventManager;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IOutboundGrpcClientFactory _clientFactory;
    private readonly ExternalWorkerOptions _options;
    private readonly HostJsonContentProvider _hostJsonContentProvider;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, WorkerConnection> _workers = new();
    private readonly ReaderWriterLockSlim _lifecycleLock = new();
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
        ILoggerFactory loggerFactory)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _hostJsonContentProvider = hostJsonContentProvider ?? throw new ArgumentNullException(nameof(hostJsonContentProvider));
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
            // until a worker is linked via POST /admin/workers/link.
            // The WebHost layer (admin APIs) remains responsive during this time.
            _logger.LogInformation("No gRPC endpoint configured. Host will wait for worker assignment via admin API.");
        }
    }

    /// <inheritdoc/>
    public async Task ConnectWorkerAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
    {
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
                    $"Worker '{workerId}' already exists. Disconnect it before reassigning.");
            }
        }
        finally
        {
            _lifecycleLock.ExitReadLock();
        }

        // The rest of connect runs outside the lock. The worker is in _workers,
        // so DrainAndDisconnectAllAsync will find it and await ConnectCompleted
        // before cleaning up.
        await ConnectWorkerCoreAsync(workerId, endpoint, worker, cancellationToken);
    }

    private async Task ConnectWorkerCoreAsync(string workerId, Uri endpoint, WorkerConnection worker, CancellationToken cancellationToken)
    {
        var info = worker.Info;

        try
        {
            _logger.LogInformation("Connecting to external worker '{workerId}' at {endpoint}.", workerId, endpoint);

            _eventManager.AddGrpcChannels(workerId);

            var client = _clientFactory.Create();
            worker.Client = client;

            await client.ConnectAsync(workerId, endpoint, cancellationToken);

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
            await channel.StartWorkerProcessAsync(cancellationToken);

            _logger.LogInformation("Waiting for worker '{workerId}' init handshake.", workerId);
            await channel.WaitForInitAsync(InitTimeout, cancellationToken);

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

            // Register the channel directly — no event needed.
            _channelManager.AddChannel(workerId, channel);

            // Subscribe to drain signals from the worker proxy.
            channel.DrainRequested += OnWorkerDrainRequested;

            _logger.LogInformation("Worker '{workerId}' connected and registered.", workerId);

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
                    _logger.LogInformation("First worker connected. Starting ScriptHost.");
                    await _scriptHostManager.StartAsync(cancellationToken);
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
                await _scriptHostStarted.Task.WaitAsync(cancellationToken);

                if (Utility.TryGetHostService(_scriptHostManager, out ConnectedWorkerInvocationDispatcher dispatcher))
                {
                    dispatcher.SetupChannel(channel);
                    _logger.LogDebug("SetupChannel called for subsequent worker '{workerId}'.", workerId);
                }
            }

            info.State = WorkerConnectionState.Connected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect worker '{workerId}'.", workerId);

            // Clean up partially-created resources so the platform can retry
            // after calling DELETE to clear the Error state.
            await CleanupWorkerResourcesAsync(workerId, worker);

            info.State = WorkerConnectionState.Error;
            info.ErrorMessage = ex.Message;

            throw;
        }
        finally
        {
            worker.ConnectCompleted.TrySetResult();
        }
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

            await CleanupWorkerResourcesAsync(workerId, worker);

            // Remove from tracking only after cleanup succeeds.
            _workers.TryRemove(workerId, out _);
            _logger.LogInformation("Worker '{workerId}' disconnected.", workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting worker '{workerId}'.", workerId);

            worker.Info.State = WorkerConnectionState.Error;
            worker.Info.ErrorMessage = ex.Message;

            throw;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkerConnectionInfo> GetWorkerStatuses()
        => _workers.Values.Select(w => w.Info).ToList().AsReadOnly();

    /// <inheritdoc/>
    public WorkerConnectionInfo GetWorkerStatus(string workerId)
        => _workers.TryGetValue(workerId, out var worker) ? worker.Info : null;

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
            workerIds = _workers.Keys.ToList();
        }
        finally
        {
            _lifecycleLock.ExitWriteLock();
        }

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
        /// Gets a signal that indicates when <see cref="ConnectWorkerAsync"/> has
        /// finished (success or failure).
        /// <see cref="DisconnectWorkerAsync"/> awaits this before cleaning up to avoid
        /// racing with an in-flight connection.
        /// </summary>
        public TaskCompletionSource ConnectCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Atomically claims the disconnect operation. Returns <see langword="true"/> if this
        /// caller is the first to claim it; subsequent callers get <see langword="false"/>.
        /// </summary>
        public bool TryClaimDisconnect() => Interlocked.CompareExchange(ref _disconnecting, 1, 0) == 0;
    }
}
