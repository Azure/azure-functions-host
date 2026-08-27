// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates raw FunctionRpc client connections over an injected duplex-channel factory.
/// </summary>
internal sealed class RpcClientConnectionFactory : IRpcClientConnectionFactory
{
    private readonly IDuplexChannelFactory<StreamingMessage> _channelFactory;
    private readonly ILogger<RpcClientConnection> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RpcClientConnectionFactory"/> class.
    /// </summary>
    /// <param name="channelFactory">The duplex-channel factory.</param>
    /// <param name="logger">The connection logger.</param>
    public RpcClientConnectionFactory(IDuplexChannelFactory<StreamingMessage> channelFactory,
        ILogger<RpcClientConnection> logger)
    {
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RpcClientConnection> ConnectAsync(RpcClientConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        Channel<StreamingMessage> channel = await _channelFactory.ConnectAsync(options.Endpoint, cancellationToken);
        return new RpcClientConnection(options.WorkerId, channel, _logger);
    }
}
