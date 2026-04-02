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

    private readonly ConnectedWorkerChannelFactory _channelFactory;
    private readonly IConnectedWorkerChannelManager _channelManager;
    private readonly IScriptEventManager _eventManager;
    private readonly IScriptHostManager _scriptHostManager;
    private readonly ExternalWorkerOptions _options;
    private readonly HostJsonContentProvider _hostJsonContentProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, OutboundGrpcClient> _clients = new();
    private readonly ConcurrentDictionary<string, WorkerConnectionInfo> _workerStates = new();
    private readonly TaskCompletionSource _scriptHostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerConnectionService"/> class.
    /// </summary>
    public WorkerConnectionService(
        ConnectedWorkerChannelFactory channelFactory,
        IConnectedWorkerChannelManager channelManager,
        IScriptEventManager eventManager,
        IScriptHostManager scriptHostManager,
        IOptions<ExternalWorkerOptions> options,
        HostJsonContentProvider hostJsonContentProvider,
        ILoggerFactory loggerFactory)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _scriptHostManager = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _hostJsonContentProvider = hostJsonContentProvider ?? throw new ArgumentNullException(nameof(hostJsonContentProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<WorkerConnectionService>();
    }

    /// <inheritdoc/>
    public int ActiveWorkerCount => _workerStates.Values.Count(s => s.State == WorkerConnectionState.Connected);

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
            // until a worker is assigned via POST /admin/workers/assign.
            // The WebHost layer (admin APIs) remains responsive during this time.
            _logger.LogInformation("No gRPC endpoint configured. Host will wait for worker assignment via admin API.");
        }
    }

    /// <inheritdoc/>
    public async Task ConnectWorkerAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
    {
        _workerStates[workerId] = new WorkerConnectionInfo
        {
            WorkerId = workerId,
            State = WorkerConnectionState.Connecting
        };

        try
        {
            _logger.LogInformation("Connecting to external worker '{workerId}' at {endpoint}.", workerId, endpoint);

            _eventManager.AddGrpcChannels(workerId);

            var client = new OutboundGrpcClient(_eventManager, _loggerFactory.CreateLogger<OutboundGrpcClient>());
            _clients[workerId] = client;

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
            if (_scriptHostStarted.TrySetResult())
            {
                // First worker: start the ScriptHost. In external worker mode,
                // WebJobsScriptHostService is not registered as an IHostedService,
                // so the ScriptHost hasn't started yet. Now that a worker has delivered
                // host.json and registered a channel, WaitForContent and WaitForChannelAsync
                // will return immediately when the ScriptHost builds.
                _logger.LogInformation("First worker connected. Starting ScriptHost.");
                await ((IHostedService)_scriptHostManager).StartAsync(cancellationToken);
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

            _workerStates[workerId] = new WorkerConnectionInfo
            {
                WorkerId = workerId,
                State = WorkerConnectionState.Connected
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect worker '{workerId}'.", workerId);

            _workerStates[workerId] = new WorkerConnectionInfo
            {
                WorkerId = workerId,
                State = WorkerConnectionState.Error,
                ErrorMessage = ex.Message
            };

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectWorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disconnecting worker '{workerId}'.", workerId);

        _workerStates[workerId] = new WorkerConnectionInfo
        {
            WorkerId = workerId,
            State = WorkerConnectionState.Draining
        };

        try
        {
            // ShutdownChannelAsync drains in-flight invocations, then disposes the channel.
            await _channelManager.ShutdownChannelAsync(workerId);

            // Dispose the outbound gRPC client.
            if (_clients.TryRemove(workerId, out var client))
            {
                await client.DisposeAsync();
            }

            _workerStates[workerId] = new WorkerConnectionInfo
            {
                WorkerId = workerId,
                State = WorkerConnectionState.Disconnected
            };

            _logger.LogInformation("Worker '{workerId}' disconnected.", workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting worker '{workerId}'.", workerId);

            _workerStates[workerId] = new WorkerConnectionInfo
            {
                WorkerId = workerId,
                State = WorkerConnectionState.Error,
                ErrorMessage = ex.Message
            };

            throw;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkerConnectionInfo> GetWorkerStatuses()
        => _workerStates.Values.ToList().AsReadOnly();

    /// <inheritdoc/>
    public WorkerConnectionInfo GetWorkerStatus(string workerId)
        => _workerStates.TryGetValue(workerId, out var info) ? info : null;

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var kvp in _clients)
        {
            await kvp.Value.DisposeAsync();
        }

        _clients.Clear();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _clients)
        {
            await kvp.Value.DisposeAsync();
        }

        _clients.Clear();
    }
}
