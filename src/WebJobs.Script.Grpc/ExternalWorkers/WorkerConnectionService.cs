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
internal class WorkerConnectionService : IHostedService, IWorkerConnectionManager, IAsyncDisposable
{
    private static readonly TimeSpan InitTimeout = TimeSpan.FromMinutes(2);

    private readonly IConnectedWorkerChannelFactory _channelFactory;
    private readonly IConnectedWorkerChannelManager _channelManager;
    private readonly IScriptEventManager _eventManager;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly IOutboundGrpcClientFactory _clientFactory;
    private readonly ExternalWorkerOptions _options;
    private readonly HostJsonContentProvider _hostJsonContentProvider;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, WorkerConnection> _workers = new();
    private readonly TaskCompletionSource _scriptHostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _firstWorkerClaimed;

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
        if (_workers.ContainsKey(workerId))
        {
            throw new InvalidOperationException(
                $"Worker '{workerId}' already exists. Disconnect it before reassigning.");
        }

        var info = new WorkerConnectionInfo
        {
            WorkerId = workerId,
            State = WorkerConnectionState.Connecting
        };

        var worker = new WorkerConnection { Info = info };
        _workers[workerId] = worker;

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
            _logger.LogInformation("Worker '{workerId}' connected and registered.", workerId);

            // Start or update the ScriptHost based on whether this is the first worker.
            // The first caller starts the ScriptHost; concurrent callers block until
            // startup completes, then call SetupChannel on the dispatcher.
            if (Interlocked.CompareExchange(ref _firstWorkerClaimed, 1, 0) == 0)
            {
                // First worker: start the ScriptHost. In external worker mode,
                // WebJobsScriptHostService is not registered as an IHostedService,
                // so the ScriptHost hasn't started yet. Now that a worker has delivered
                // host.json and registered a channel, WaitForContent and WaitForChannelAsync
                // will return immediately when the ScriptHost builds.
                try
                {
                    _logger.LogInformation("First worker connected. Starting ScriptHost.");
                    await _scriptHostManager.StartAsync(cancellationToken);
                    _scriptHostStarted.TrySetResult();
                }
                catch (Exception ex)
                {
                    _scriptHostStarted.TrySetException(ex);
                    throw;
                }
            }
            else
            {
                // Wait for the first worker's StartAsync to complete before resolving the dispatcher.
                await _scriptHostStarted.Task;

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

        // Wait for any in-flight ConnectWorkerAsync to finish before cleaning up.
        await worker.ConnectCompleted.Task;

        worker.Info.State = WorkerConnectionState.Draining;

        try
        {
            await RemoveWorkerAsync(workerId, worker);
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
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (string workerId in _workers.Keys)
        {
            if (_workers.TryGetValue(workerId, out var worker))
            {
                try
                {
                    await RemoveWorkerAsync(workerId, worker);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during graceful shutdown of worker '{workerId}'.", workerId);
                }
            }
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
    }

    /// <summary>
    /// Disposes the gRPC client, removes gRPC event channels, and drains the
    /// worker channel. Does NOT remove the worker from <see cref="_workers"/>
    /// so the Error state remains visible to callers.
    /// </summary>
    private async Task CleanupWorkerResourcesAsync(string workerId, WorkerConnection worker)
    {
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
    /// Full cleanup: disposes resources and removes the worker from tracking.
    /// Used by disconnect and stop, where the worker entry should not persist.
    /// </summary>
    private async Task RemoveWorkerAsync(string workerId, WorkerConnection worker)
    {
        await CleanupWorkerResourcesAsync(workerId, worker);
        _workers.TryRemove(workerId, out _);
    }

    /// <summary>
    /// Internal tracking type that pairs a worker's API-visible state
    /// with its gRPC client resource.
    /// </summary>
    private class WorkerConnection
    {
        public WorkerConnectionInfo Info { get; set; }

        public IOutboundGrpcClient Client { get; set; }

        /// <summary>
        /// Gets a signal that indicates when <see cref="ConnectWorkerAsync"/> has
        /// finished (success or failure).
        /// <see cref="DisconnectWorkerAsync"/> awaits this before cleaning up to avoid
        /// racing with an in-flight connection.
        /// </summary>
        public TaskCompletionSource ConnectCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
