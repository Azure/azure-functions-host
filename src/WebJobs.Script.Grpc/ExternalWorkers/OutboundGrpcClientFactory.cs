// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Default factory that creates <see cref="OutboundGrpcClient"/> instances.
/// </summary>
internal sealed class OutboundGrpcClientFactory : IOutboundGrpcClientFactory
{
    private readonly IScriptEventManager _eventManager;
    private readonly ILoggerFactory _loggerFactory;

    public OutboundGrpcClientFactory(IScriptEventManager eventManager, ILoggerFactory loggerFactory)
    {
        _eventManager = eventManager;
        _loggerFactory = loggerFactory;
    }

    public IOutboundGrpcClient Create()
        => new OutboundGrpcClient(_eventManager, _loggerFactory.CreateLogger<OutboundGrpcClient>());
}
