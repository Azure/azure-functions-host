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
internal record RelayOptions(int RuntimeGrpcPort, int WorkerGrpcPort, int HttpProxyPort, string? HostJsonPath);

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

    // Channels that buffer messages flowing in each direction.
    private readonly Channel<StreamingMessage> _toWorker = Channel.CreateUnbounded<StreamingMessage>();
    private readonly Channel<StreamingMessage> _toRuntime = Channel.CreateUnbounded<StreamingMessage>();

    // Signals raised once each side has called EventStream.
    private readonly TaskCompletionSource _runtimeConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _workerConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FunctionRpcRelay(RelayOptions options, ILogger<FunctionRpcRelay> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

                    // Rewrite HttpUri to point at the proxy's HTTP port so the runtime
                    // routes HTTP requests through the proxy rather than directly to the worker.
                    if (message.WorkerInitResponse.Capabilities.ContainsKey("HttpUri"))
                    {
                        string proxyHttpUri = $"http://localhost:{_options.HttpProxyPort}";
                        message.WorkerInitResponse.Capabilities["HttpUri"] = proxyHttpUri;
                        _logger.LogInformation("Rewrote HttpUri capability to {Uri}", proxyHttpUri);
                    }
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
