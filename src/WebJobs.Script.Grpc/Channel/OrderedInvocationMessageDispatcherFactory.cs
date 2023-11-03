// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

internal class OrderedInvocationMessageDispatcherFactory : IInvocationMessageDispatcherFactory
{
    private readonly Action<InboundGrpcEvent> _processItem;
    private readonly ILogger _logger;

    public OrderedInvocationMessageDispatcherFactory(Action<InboundGrpcEvent> processItem, ILogger logger)
    {
        _processItem = processItem;
        _logger = logger;
    }

    public IInvocationMessageDispatcher Create(string invocationId) =>
        new OrderedInvocationMessageDispatcher(invocationId, _logger, _processItem);
}