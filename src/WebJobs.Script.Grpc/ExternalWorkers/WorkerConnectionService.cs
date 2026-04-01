// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
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
/// On startup, if a <see cref="ExternalWorkerOptions.GrpcEndpoint"/> is configured,
/// connects to the remote worker, waits for the init handshake to complete,
/// extracts host.json content from worker capabilities, and registers the channel
/// with the <see cref="IConnectedWorkerChannelManager"/>.
/// </summary>
internal class WorkerConnectionService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan InitTimeout = TimeSpan.FromMinutes(2);

    private readonly ConnectedWorkerChannelFactory _channelFactory;
    private readonly IConnectedWorkerChannelManager _channelManager;
    private readonly IScriptEventManager _eventManager;
    private readonly ExternalWorkerOptions _options;
    private readonly HostJsonContentProvider _hostJsonContentProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, OutboundGrpcClient> _clients = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerConnectionService"/> class.
    /// </summary>
    public WorkerConnectionService(
        ConnectedWorkerChannelFactory channelFactory,
        IConnectedWorkerChannelManager channelManager,
        IScriptEventManager eventManager,
        IOptions<ExternalWorkerOptions> options,
        HostJsonContentProvider hostJsonContentProvider,
        ILoggerFactory loggerFactory)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _hostJsonContentProvider = hostJsonContentProvider ?? throw new ArgumentNullException(nameof(hostJsonContentProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<WorkerConnectionService>();
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogDebug("External worker connections are not enabled.");
            return;
        }

        if (_options.GrpcEndpoint is not null)
        {
            string workerId = $"w_{Guid.NewGuid().ToString("N")[..8]}";
            await ConnectWorkerAsync(workerId, new Uri(_options.GrpcEndpoint), cancellationToken);
        }
    }

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

    private async Task ConnectWorkerAsync(string workerId, Uri endpoint, CancellationToken cancellationToken)
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
    }
}
