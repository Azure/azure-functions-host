// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Contains the disposable gRPC call and its inbound message-pump task.
/// </summary>
internal sealed record OutboundGrpcStreamConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboundGrpcStreamConnection"/> class.
    /// </summary>
    /// <param name="call">The active gRPC call.</param>
    /// <param name="inboundPumpTask">The task that pumps inbound messages for the call.</param>
    public OutboundGrpcStreamConnection(IDisposable call, Task inboundPumpTask)
    {
        Call = call;
        InboundPumpTask = inboundPumpTask;
    }

    /// <summary>
    /// Gets the active gRPC call.
    /// </summary>
    public IDisposable Call { get; }

    /// <summary>
    /// Gets the task that pumps inbound messages for the call.
    /// </summary>
    public Task InboundPumpTask { get; }
}
