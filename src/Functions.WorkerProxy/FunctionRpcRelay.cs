// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Configuration for the worker proxy relay.
/// </summary>
/// <param name="RuntimeGrpcPort">Port the Functions runtime connects to.</param>
/// <param name="WorkerGrpcPort">Port the language worker connects to.</param>
/// <param name="HttpProxyPort">Port the HTTP proxy listens on (rewritten into HttpUri capability).</param>
/// <param name="HostJsonPath">Optional path to a host.json to inject into WorkerInitResponse capabilities.</param>
/// <param name="HttpProxyEndpoint">
/// Full URL advertised in the <c>HttpUri</c> capability so the runtime routes HTTP
/// requests through this proxy. Defaults to <c>http://localhost:{HttpProxyPort}</c>;
/// override when running in containers where <c>localhost</c> is not reachable across
/// container boundaries.
/// </param>
internal record RelayOptions(int RuntimeGrpcPort, int WorkerGrpcPort, int HttpProxyPort, string? HostJsonPath, string HttpProxyEndpoint);

/// <summary>
/// Relays <see cref="StreamingMessage"/> payloads bidirectionally between the
/// Functions runtime and a language worker over two independent gRPC EventStream
/// connections. When a <c>WorkerInitResponse</c> flows from the worker to the
/// runtime and a host.json path is configured, the file contents are injected
/// into the response's capabilities map under the key <c>host_configuration_json</c>.
/// </summary>
internal sealed class FunctionRpcRelay : FunctionRpc.FunctionRpcBase
{
    private readonly RelayOptions _options;
    private readonly ILogger<FunctionRpcRelay> _logger;
    private readonly WorkerPodStateManager _stateManager;

    // Channels that buffer messages flowing in each direction.
    private readonly Channel<StreamingMessage> _toWorker = Channel.CreateUnbounded<StreamingMessage>();
    private readonly Channel<StreamingMessage> _toRuntime = Channel.CreateUnbounded<StreamingMessage>();

    // Signals raised once each side has called EventStream.
    private readonly TaskCompletionSource _runtimeConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _workerConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FunctionRpcRelay(RelayOptions options, ILogger<FunctionRpcRelay> logger, WorkerPodStateManager stateManager)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    /// <summary>
    /// Sends a <c>WorkerDrainRequest</c> to the runtime over the gRPC stream.
    /// Called when NNA calls <c>POST /drain</c> on the worker proxy.
    /// </summary>
    public async Task SendDrainRequestToRuntimeAsync()
    {
        var message = new StreamingMessage
        {
            WorkerDrainRequest = new WorkerDrainRequest()
        };

        await _toRuntime.Writer.WriteAsync(message);
        _logger.LogInformation("Sent WorkerDrainRequest to runtime.");
    }

    /// <inheritdoc />
    public override async Task EventStream(
        IAsyncStreamReader<StreamingMessage> requestStream,
        IServerStreamWriter<StreamingMessage> responseStream,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        int localPort = httpContext.Connection.LocalPort;

        if (localPort == _options.RuntimeGrpcPort)
        {
            _logger.LogInformation("Runtime connected on port {Port}", localPort);
            _runtimeConnected.TrySetResult();
            await _workerConnected.Task.WaitAsync(context.CancellationToken);

            // Runtime reads from _toRuntime, writes into _toWorker.
            await RelayAsync(requestStream, responseStream, _toWorker, _toRuntime, "runtime", context.CancellationToken);
        }
        else
        {
            _logger.LogInformation("Worker connected on port {Port}", localPort);
            _workerConnected.TrySetResult();
            _stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
            _stateManager.UpdateHealthStatus(WorkerPodHealthStatus.Healthy);
            await _runtimeConnected.Task.WaitAsync(context.CancellationToken);

            // Worker reads from _toWorker, writes into _toRuntime.
            await RelayAsync(requestStream, responseStream, _toRuntime, _toWorker, "worker", context.CancellationToken);
        }
    }

    private async Task RelayAsync(
        IAsyncStreamReader<StreamingMessage> inbound,
        IServerStreamWriter<StreamingMessage> outbound,
        Channel<StreamingMessage> sendChannel,
        Channel<StreamingMessage> receiveChannel,
        string side,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var readTask = ReadInboundAsync(inbound, sendChannel.Writer, side, cts.Token);
        var writeTask = WriteOutboundAsync(receiveChannel.Reader, outbound, cts.Token);

        try
        {
            // When either direction ends, tear down both.
            await Task.WhenAny(readTask, writeTask);
        }
        finally
        {
            await cts.CancelAsync();

            // Observe both tasks to prevent unhandled exceptions.
            try { await readTask; } catch { }
            try { await writeTask; } catch { }

            _logger.LogInformation("[{Side}] stream disconnected", side);
        }
    }

    private async Task ReadInboundAsync(
        IAsyncStreamReader<StreamingMessage> inbound,
        ChannelWriter<StreamingMessage> writer,
        string side,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await inbound.MoveNext(cancellationToken))
            {
                var message = inbound.Current;
                _logger.LogDebug("[{Side}] → {Content}", side, message.ContentCase);

                if (string.Equals(side, "worker", StringComparison.Ordinal)
                    && message.ContentCase == StreamingMessage.ContentOneofCase.WorkerInitResponse)
                {
                    if (!string.IsNullOrEmpty(_options.HostJsonPath))
                    {
                        string hostJson = await File.ReadAllTextAsync(_options.HostJsonPath, cancellationToken);
                        message.WorkerInitResponse.Capabilities["host_configuration_json"] = hostJson;
                        _logger.LogInformation("Injected host.json into WorkerInitResponse capabilities");
                    }

                    // Rewrite HttpUri to point at the proxy's HTTP endpoint so the runtime
                    // routes HTTP requests through the proxy rather than directly to the worker.
                    if (message.WorkerInitResponse.Capabilities.ContainsKey("HttpUri"))
                    {
                        message.WorkerInitResponse.Capabilities["HttpUri"] = _options.HttpProxyEndpoint;
                        _logger.LogInformation("Rewrote HttpUri capability to {Uri}", _options.HttpProxyEndpoint);
                    }
                }

                // Intercept WorkerDrainRequest from runtime — update state machine.
                // In runtime-initiated stop, the runtime sends this to notify
                // the proxy to enter Draining state. Do NOT forward to the language worker.
                if (string.Equals(side, "runtime", StringComparison.Ordinal)
                    && message.ContentCase == StreamingMessage.ContentOneofCase.WorkerDrainRequest)
                {
                    _logger.LogInformation("Received WorkerDrainRequest from runtime.");
                    _stateManager.UpdatePodStatus(WorkerPodStatus.Draining);
                    continue;
                }

                // Intercept WorkerDrainComplete from runtime — update state machine.
                // Do NOT forward drain messages to the language worker.
                if (string.Equals(side, "runtime", StringComparison.Ordinal)
                    && message.ContentCase == StreamingMessage.ContentOneofCase.WorkerDrainComplete)
                {
                    _logger.LogInformation("Received WorkerDrainComplete from runtime.");
                    _stateManager.UpdatePodStatus(WorkerPodStatus.DrainCompleted);
                    _stateManager.UpdatePodStatus(WorkerPodStatus.MarkForDeletion);
                    continue; // Don't relay drain messages to the language worker.
                }

                await writer.WriteAsync(message, cancellationToken);
            }

            writer.TryComplete();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            writer.TryComplete(ex);
            throw;
        }
        catch (OperationCanceledException)
        {
            writer.TryComplete();
        }
    }

    private static async Task WriteOutboundAsync(
        ChannelReader<StreamingMessage> reader,
        IServerStreamWriter<StreamingMessage> outbound,
        CancellationToken cancellationToken)
    {
        await foreach (var message in reader.ReadAllAsync(cancellationToken))
        {
            await outbound.WriteAsync(message);
        }
    }
}
