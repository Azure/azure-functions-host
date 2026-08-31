// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates FunctionRpc clients and reuses one connected gRPC channel for each endpoint.
/// </summary>
internal sealed class RpcClientFactory : IRpcClientFactory
{
    private const int MaxMessageLengthBytes = int.MaxValue;

    /// <summary>
    /// Gets the maximum time allowed for an individual socket connection attempt.
    /// </summary>
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the idle interval before an HTTP/2 keepalive ping is sent.
    /// </summary>
    private static readonly TimeSpan DefaultKeepAlivePingDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the time allowed for a keepalive ping acknowledgement.
    /// </summary>
    private static readonly TimeSpan DefaultKeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<Uri, GrpcChannel> _channels = [];
    private readonly Func<GrpcChannel, CancellationToken, Task> _connectAsync;
    private readonly Dictionary<Uri, SemaphoreSlim> _connectGates = [];
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger<RpcClientFactory> _logger;
    private readonly TaskCompletionSource _operationsCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly Lock _syncLock = new();
    private int _activeOperations;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger used for partial channel cleanup failures.</param>
    public RpcClientFactory(ILogger<RpcClientFactory> logger)
        : this(logger, static (channel, cancellationToken) => channel.ConnectAsync(cancellationToken))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger used for partial channel cleanup failures.</param>
    /// <param name="connectAsync">The operation used to connect a new channel. The operation must observe its cancellation token.</param>
    internal RpcClientFactory(ILogger<RpcClientFactory> logger, Func<GrpcChannel, CancellationToken, Task> connectAsync)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectAsync = connectAsync ?? throw new ArgumentNullException(nameof(connectAsync));
    }

    /// <summary>
    /// Gets the number of endpoint channels currently cached.
    /// </summary>
    internal int CachedChannelCount
    {
        get
        {
            lock (_syncLock)
            {
                return _channels.Count;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<FunctionRpc.FunctionRpcClient> CreateAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        GrpcChannel candidate = null;
        bool gateEntered = false;
        SemaphoreSlim connectGate;

        // Coordinate cache access with disposal and register this cold-path operation before releasing the lock.
        // Disposal waits for every registered operation before releasing the per-endpoint gates.
        lock (_syncLock)
        {
            ThrowIfDisposed();
            if (_channels.TryGetValue(endpoint, out GrpcChannel cachedChannel))
            {
                return new FunctionRpc.FunctionRpcClient(cachedChannel);
            }

            if (!_connectGates.TryGetValue(endpoint, out connectGate))
            {
                connectGate = new SemaphoreSlim(1, 1);
                _connectGates.Add(endpoint, connectGate);
            }

            _activeOperations++;
        }

        try
        {
            using CancellationTokenSource connectionSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownSource.Token);

            // Only one caller connects a channel for an endpoint at a time. Each caller retains its own cancellation token,
            // so canceling one attempt does not cancel another caller waiting to retry.
            await connectGate.WaitAsync(connectionSource.Token);
            gateEntered = true;

            // Another caller may have populated the cache while this caller waited for the endpoint gate.
            lock (_syncLock)
            {
                ThrowIfDisposed();
                if (_channels.TryGetValue(endpoint, out GrpcChannel cachedChannel))
                {
                    return new FunctionRpc.FunctionRpcClient(cachedChannel);
                }
            }

            candidate = CreateChannel(endpoint);
            await _connectAsync(candidate, connectionSource.Token);
            connectionSource.Token.ThrowIfCancellationRequested();

            GrpcChannel channel;
            // Publish only a fully connected channel. The lock prevents disposal from snapshotting the cache between
            // publication and ownership transfer from the local candidate.
            lock (_syncLock)
            {
                ThrowIfDisposed();
                _channels.Add(endpoint, candidate);
                channel = candidate;
                candidate = null;
            }

            return new FunctionRpc.FunctionRpcClient(channel);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(RpcClientFactory));
        }
        finally
        {
            // Release the endpoint for the next waiter, dispose any candidate that was not cached, and signal disposal
            // when this was the final active cold-path operation.
            if (gateEntered)
            {
                connectGate.Release();
            }

            TryCleanup(() => candidate?.Dispose(), endpoint, "dispose the unused gRPC channel");

            // Disposal waits for all cold-path operations to leave this block before releasing the connection gates and
            // shutdown source. The final operation completes that shared wait while holding the count's lock.
            lock (_syncLock)
            {
                _activeOperations--;
                if (_disposed && _activeOperations == 0)
                {
                    _operationsCompleted.TrySetResult();
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GrpcChannel[] channels = null;
        bool startDisposal = false;

        lock (_syncLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                channels = _channels.Values.ToArray();
                _channels.Clear();
                if (_activeOperations == 0)
                {
                    _operationsCompleted.TrySetResult();
                }

                startDisposal = true;
            }
        }

        if (startDisposal)
        {
            _ = CompleteDisposalAsync(channels);
        }

        return new ValueTask(_disposeCompletion.Task);
    }

    /// <summary>
    /// Creates the HTTP handler used by each cached gRPC channel with transport-level connection and keepalive settings.
    /// </summary>
    /// <returns>A handler whose ownership transfers to its gRPC channel.</returns>
    internal static SocketsHttpHandler CreateHttpHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = DefaultConnectTimeout,
            KeepAlivePingDelay = DefaultKeepAlivePingDelay,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingTimeout = DefaultKeepAlivePingTimeout,
        };
    }

    /// <summary>
    /// Validates that an endpoint can identify a gRPC service authority without call-specific URI components.
    /// </summary>
    /// <param name="endpoint">The endpoint to validate.</param>
    internal static void ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri ||
            (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ArgumentException("The endpoint must be an absolute HTTP or HTTPS URI with a host.", nameof(endpoint));
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.Equals(endpoint.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("The endpoint must not include user information, a path, a query, or a fragment.", nameof(endpoint));
        }
    }

    private static Exception CaptureCleanupFailure(Exception currentException, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            return currentException is null ? exception : new AggregateException(currentException, exception);
        }

        return currentException;
    }

    private async Task CompleteDisposalAsync(IReadOnlyCollection<GrpcChannel> channels)
    {
        try
        {
            await DisposeCoreAsync(channels);
            _disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposeCompletion.TrySetException(exception);
        }
    }

    private GrpcChannel CreateChannel(Uri endpoint)
    {
        SocketsHttpHandler handler = CreateHttpHandler();

        try
        {
            return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
            {
                DisposeHttpClient = true,
                HttpHandler = handler,
                MaxReceiveMessageSize = MaxMessageLengthBytes,
                MaxSendMessageSize = MaxMessageLengthBytes,
            });
        }
        catch
        {
            TryCleanup(handler.Dispose, endpoint, "dispose the HTTP handler");
            throw;
        }
    }

    private async Task DisposeCoreAsync(IReadOnlyCollection<GrpcChannel> channels)
    {
        Exception cleanupException = null;

        try
        {
            await _shutdownSource.CancelAsync();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        foreach (GrpcChannel channel in channels)
        {
            cleanupException = CaptureCleanupFailure(cleanupException, channel.Dispose);
        }

        await _operationsCompleted.Task;

        SemaphoreSlim[] connectGates;
        lock (_syncLock)
        {
            connectGates = _connectGates.Values.ToArray();
            _connectGates.Clear();
        }

        foreach (SemaphoreSlim connectGate in connectGates)
        {
            cleanupException = CaptureCleanupFailure(cleanupException, connectGate.Dispose);
        }

        cleanupException = CaptureCleanupFailure(cleanupException, _shutdownSource.Dispose);

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void TryCleanup(Action cleanup, Uri endpoint, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "FunctionRpc channel cleanup failed for endpoint {Endpoint} while attempting to {CleanupOperation}.",
                endpoint, operation);
        }
    }
}
