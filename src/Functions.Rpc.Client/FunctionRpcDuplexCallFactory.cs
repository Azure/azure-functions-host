// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates outbound FunctionRpc duplex calls.
/// </summary>
internal sealed class FunctionRpcDuplexCallFactory : IDuplexCallFactory<StreamingMessage, StreamingMessage>
{
    /// <summary>
    /// Gets the maximum time allowed for an individual socket connection attempt.
    /// </summary>
    internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the idle interval before an HTTP/2 keepalive ping is sent.
    /// </summary>
    internal static readonly TimeSpan DefaultKeepAlivePingDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the time allowed for a keepalive ping acknowledgement.
    /// </summary>
    internal static readonly TimeSpan DefaultKeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    private const int MaxMessageLengthBytes = int.MaxValue;

    private readonly ILogger _duplexCallLogger;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcDuplexCallFactory"/> class.
    /// </summary>
    /// <param name="loggerFactory">The factory used to create transport and adapter loggers.</param>
    public FunctionRpcDuplexCallFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<FunctionRpcDuplexCallFactory>();
        _duplexCallLogger = loggerFactory.CreateLogger<GrpcDuplexCall<StreamingMessage, StreamingMessage>>();
    }

    /// <inheritdoc />
    public async Task<IDuplexCall<StreamingMessage, StreamingMessage>> ConnectAsync(Uri endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        SocketsHttpHandler handler = CreateHttpHandler();
        GrpcChannel channel = null;
        IDisposable call = null;
        CancellationTokenSource callLifetimeSource = null;

        try
        {
            channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
            {
                DisposeHttpClient = true,
                HttpHandler = handler,
                MaxReceiveMessageSize = MaxMessageLengthBytes,
                MaxSendMessageSize = MaxMessageLengthBytes,
            });
            handler = null;

            await channel.ConnectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            callLifetimeSource = new CancellationTokenSource();
            FunctionRpc.FunctionRpcClient client = new(channel);
            var grpcCall = client.EventStream(cancellationToken: callLifetimeSource.Token);
            call = grpcCall;

            GrpcDuplexCall<StreamingMessage, StreamingMessage> duplexCall = new(grpcCall, callLifetimeSource, channel, _duplexCallLogger);
            call = null;
            callLifetimeSource = null;
            channel = null;

            return duplexCall;
        }
        catch
        {
            TryCleanup(() => call?.Dispose(), "dispose the SDK duplex call");
            TryCleanup(() => callLifetimeSource?.Cancel(), "cancel the call lifetime");
            TryCleanup(() => callLifetimeSource?.Dispose(), "dispose the call lifetime");
            TryCleanup(() => channel?.Dispose(), "dispose the gRPC channel");
            TryCleanup(() => handler?.Dispose(), "dispose the HTTP handler");
            throw;
        }
    }

    /// <summary>
    /// Creates the HTTP handler used by the gRPC channel with transport-level connection and keepalive settings.
    /// </summary>
    /// <returns>A handler whose ownership transfers to the gRPC channel.</returns>
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

    private void TryCleanup(Action cleanup, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "FunctionRpc cleanup failed while attempting to {CleanupOperation}.", operation);
        }
    }
}
