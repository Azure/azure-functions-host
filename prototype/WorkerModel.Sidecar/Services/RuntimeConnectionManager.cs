using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace WorkerModel.Sidecar.Services;

/// <summary>
/// Manages the gRPC connection to the assigned Runtime.
/// Only connects after receiving /assign with RuntimeEndpoint.
/// Uses the actual FunctionRpc types from WebJobs.Script.Grpc.
/// </summary>
public class RuntimeConnectionManager : IAsyncDisposable
{
    private readonly WorkerState _workerState;
    private readonly ILogger<RuntimeConnectionManager> _logger;
    private GrpcChannel? _runtimeChannel;
    private AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage>? _runtimeStream;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    // Channels for bidirectional message passing
    private Channel<StreamingMessage>? _toRuntimeChannel;
    private Channel<StreamingMessage>? _fromRuntimeChannel;

    public RuntimeConnectionManager(WorkerState workerState, ILogger<RuntimeConnectionManager> logger)
    {
        _workerState = workerState;
        _logger = logger;
    }

    /// <summary>
    /// Gets whether we're connected to a Runtime.
    /// </summary>
    public bool IsConnected => _runtimeChannel is not null && _runtimeStream is not null;

    /// <summary>
    /// Gets the channel for sending messages to the Runtime.
    /// </summary>
    public ChannelWriter<StreamingMessage>? ToRuntime => _toRuntimeChannel?.Writer;

    /// <summary>
    /// Gets the channel for receiving messages from the Runtime.
    /// </summary>
    public ChannelReader<StreamingMessage>? FromRuntime => _fromRuntimeChannel?.Reader;

    /// <summary>
    /// Connects to the Runtime at the specified endpoint.
    /// Called after /assign provides the RuntimeEndpoint.
    /// </summary>
    public async Task ConnectAsync(string runtimeEndpoint, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                _logger.LogInformation("[RuntimeConnection] Already connected to Runtime");
                return;
            }

            _logger.LogInformation("[RuntimeConnection] Connecting to Runtime at {Endpoint}...", runtimeEndpoint);

            // Create gRPC channel to Runtime
            _runtimeChannel = GrpcChannel.ForAddress(runtimeEndpoint, new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 128 * 1024 * 1024, // 128 MB
                MaxSendMessageSize = 128 * 1024 * 1024
            });

            // Create the bidirectional stream using the actual FunctionRpc service
            var client = new FunctionRpc.FunctionRpcClient(_runtimeChannel);
            _runtimeStream = client.EventStream(cancellationToken: cancellationToken);

            // Create channels for message passing
            _toRuntimeChannel = Channel.CreateUnbounded<StreamingMessage>();
            _fromRuntimeChannel = Channel.CreateUnbounded<StreamingMessage>();

            // Start background tasks for stream relay
            _ = RelayToRuntimeAsync(cancellationToken);
            _ = RelayFromRuntimeAsync(cancellationToken);

            _logger.LogInformation("[RuntimeConnection] Connected to Runtime successfully");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Sends a message to the Runtime.
    /// </summary>
    public async Task SendToRuntimeAsync(StreamingMessage message, CancellationToken cancellationToken)
    {
        if (_toRuntimeChannel is null)
        {
            throw new InvalidOperationException("Not connected to Runtime");
        }

        await _toRuntimeChannel.Writer.WriteAsync(message, cancellationToken);
    }

    private async Task RelayToRuntimeAsync(CancellationToken cancellationToken)
    {
        if (_toRuntimeChannel is null || _runtimeStream is null)
        {
            return;
        }

        try
        {
            await foreach (var message in _toRuntimeChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await _runtimeStream.RequestStream.WriteAsync(message, cancellationToken);
                _logger.LogDebug("[RuntimeConnection] Sent message to Runtime: {ContentCase}", message.ContentCase);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RuntimeConnection] Error relaying to Runtime");
        }
    }

    private async Task RelayFromRuntimeAsync(CancellationToken cancellationToken)
    {
        if (_fromRuntimeChannel is null || _runtimeStream is null)
        {
            return;
        }

        try
        {
            await foreach (var message in _runtimeStream.ResponseStream.ReadAllAsync(cancellationToken))
            {
                await _fromRuntimeChannel.Writer.WriteAsync(message, cancellationToken);
                _logger.LogDebug("[RuntimeConnection] Received message from Runtime: {ContentCase}", message.ContentCase);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RuntimeConnection] Error relaying from Runtime");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _toRuntimeChannel?.Writer.Complete();
        _fromRuntimeChannel?.Writer.Complete();

        if (_runtimeStream is not null)
        {
            await _runtimeStream.RequestStream.CompleteAsync();
            _runtimeStream.Dispose();
        }

        _runtimeChannel?.Dispose();
    }
}
