// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Default factory that creates outbound clients for the FunctionRpc and extension relays.
/// </summary>
internal sealed class OutboundGrpcClientFactory : IOutboundGrpcClientFactory
{
    private readonly IScriptEventManager _eventManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IExtensionRpcEndpointRouter _extensionRpcEndpointRouter;

    /// <summary>
    /// Initializes a factory with extension endpoint routing enabled.
    /// </summary>
    /// <param name="eventManager">The event manager containing worker message channels.</param>
    /// <param name="loggerFactory">The factory used to create client loggers.</param>
    /// <param name="extensionRpcEndpointRouter">The router for registered extension endpoints.</param>
    public OutboundGrpcClientFactory(
        IScriptEventManager eventManager,
        ILoggerFactory loggerFactory,
        IExtensionRpcEndpointRouter extensionRpcEndpointRouter)
    {
        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _extensionRpcEndpointRouter = extensionRpcEndpointRouter
            ?? throw new ArgumentNullException(nameof(extensionRpcEndpointRouter));
    }

    /// <summary>
    /// Initializes a factory with extension endpoint routing disabled.
    /// </summary>
    /// <param name="eventManager">The event manager containing worker message channels.</param>
    /// <param name="loggerFactory">The factory used to create client loggers.</param>
    internal OutboundGrpcClientFactory(IScriptEventManager eventManager, ILoggerFactory loggerFactory)
        : this(eventManager, loggerFactory, new UnavailableExtensionRpcEndpointRouter())
    {
    }

    /// <inheritdoc/>
    public IOutboundGrpcClient Create()
    {
        return new OutboundGrpcClient(
            _eventManager,
            _loggerFactory.CreateLogger<OutboundGrpcClient>(),
            _extensionRpcEndpointRouter);
    }
}
