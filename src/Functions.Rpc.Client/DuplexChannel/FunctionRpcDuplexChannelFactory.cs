// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates outbound FunctionRpc duplex channels.
/// </summary>
internal sealed class FunctionRpcDuplexChannelFactory : IDuplexChannelFactory<StreamingMessage>
{
    private readonly IRpcClientFactory _clientFactory;
    private readonly ILogger<FunctionRpcDuplexChannelFactory> _logger;
    private readonly ITokenServiceProxyRequestHandler _proxyHandler;
    private readonly TokenServiceProxyOptions _proxyOptions;
    private readonly TimeProvider _timeProvider;
    private readonly TokenServiceProxyAdmission _proxyAdmission;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcDuplexChannelFactory"/> class.
    /// </summary>
    /// <param name="clientFactory">The factory that owns reusable endpoint channels.</param>
    /// <param name="logger">The logger used for partial connection cleanup failures.</param>
    public FunctionRpcDuplexChannelFactory(IRpcClientFactory clientFactory, ILogger<FunctionRpcDuplexChannelFactory> logger,
        IOptions<TokenServiceProxyOptions> proxyOptions = null, ITokenServiceProxyRequestHandler proxyHandler = null,
        TimeProvider timeProvider = null, TokenServiceProxyAdmission proxyAdmission = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _proxyOptions = proxyOptions?.Value ?? new();
        _proxyHandler = proxyHandler;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _proxyAdmission = proxyAdmission ?? new(_proxyOptions.MaxHostRequests, _proxyOptions.MaxHostBufferedBytes);
    }

    /// <inheritdoc />
    public async Task<DuplexChannel<StreamingMessage>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        if (_proxyOptions.Enabled && _proxyHandler is null)
        {
            throw new InvalidOperationException("An enabled Token Service proxy requires a root-owned HTTP request handler.");
        }

        FunctionRpc.FunctionRpcClient client = await _clientFactory.CreateAsync(endpoint, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // This token only bounds channel creation. Disposing the returned channel cancels the long-lived RPC call.
        var call = client.EventStream();
        try
        {
            GrpcDuplexChannel<StreamingMessage> channel = call.AsChannel();
            return _proxyOptions.Enabled
                ? new TokenServiceProxyDuplexChannel(channel, _proxyHandler, _proxyOptions, _timeProvider, _proxyAdmission, _logger)
                : channel;
        }
        catch
        {
            TryCleanup(call.Dispose, endpoint, "dispose the SDK duplex call");
            throw;
        }
    }

    private void TryCleanup(Action cleanup, Uri endpoint, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "FunctionRpc cleanup failed for endpoint {Endpoint} while attempting to {CleanupOperation}.",
                endpoint, operation);
        }
    }
}
