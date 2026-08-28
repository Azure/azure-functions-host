// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates outbound FunctionRpc duplex streams.
/// </summary>
internal sealed class FunctionRpcDuplexStreamFactory : IDuplexStreamFactory<StreamingMessage>
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

    private readonly ILogger<FunctionRpcDuplexStreamFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcDuplexStreamFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger used for partial connection cleanup failures.</param>
    public FunctionRpcDuplexStreamFactory(ILogger<FunctionRpcDuplexStreamFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Channel<StreamingMessage>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);

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

            GrpcDuplexStream<StreamingMessage> duplexStream = grpcCall.AsDuplexStream(callLifetimeSource, channel);
            call = null;
            callLifetimeSource = null;
            channel = null;

            return duplexStream;
        }
        catch
        {
            TryCleanup(() => callLifetimeSource?.Cancel(), "cancel the call lifetime");
            TryCleanup(() => call?.Dispose(), "dispose the SDK duplex call");
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
