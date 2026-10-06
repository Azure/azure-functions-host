// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Coordinates worker system-log formatting and output.
/// </summary>
internal sealed class WorkerSystemLogSink(
    TimeProvider timeProvider,
    WorkerSystemLogFormatter formatter,
    IWorkerSystemLogWriter writer)
    : IWorkerSystemLogSink
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly WorkerSystemLogFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly IWorkerSystemLogWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <inheritdoc />
    public ValueTask EmitAsync(RpcLog rpcLog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rpcLog);

        string record = _formatter.Format(rpcLog, _timeProvider.GetUtcNow());

        return _writer.WriteAsync(record, cancellationToken);
    }
}
