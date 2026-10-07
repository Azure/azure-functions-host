// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Coordinates worker system-log formatting and queue admission.
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
    public WorkerSystemLogEnqueueResult TryEmit(RpcLog rpcLog)
    {
        ArgumentNullException.ThrowIfNull(rpcLog);

        string record = _formatter.Format(rpcLog, _timeProvider.GetUtcNow());

        return _writer.TryEnqueue(record);
    }
}
