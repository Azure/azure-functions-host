// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates raw FunctionRpc client connections over an injected duplex-call factory.
/// </summary>
internal sealed class RpcClientConnectionFactory : IRpcClientConnectionFactory
{
    private readonly IDuplexCallFactory<StreamingMessage, StreamingMessage> _callFactory;
    private readonly ILogger<RpcClientConnection> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientConnectionFactory"/> class.
    /// </summary>
    /// <param name="callFactory">The duplex-call factory.</param>
    /// <param name="logger">The connection logger.</param>
    public RpcClientConnectionFactory(IDuplexCallFactory<StreamingMessage, StreamingMessage> callFactory,
        ILogger<RpcClientConnection> logger)
    {
        _callFactory = callFactory ?? throw new ArgumentNullException(nameof(callFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RpcClientConnection> ConnectAsync(RpcClientConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        IDuplexCall<StreamingMessage, StreamingMessage> call = await _callFactory.ConnectAsync(options.Endpoint, cancellationToken);
        return new RpcClientConnection(options.WorkerId, call, _logger);
    }
}
