// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Queues formatted worker system-log records for platform output.
/// </summary>
internal interface IWorkerSystemLogWriter
{
    /// <summary>
    /// Attempts to enqueue one complete worker system-log record.
    /// </summary>
    /// <param name="record">The formatted record.</param>
    /// <returns>The queue admission result.</returns>
    WorkerSystemLogEnqueueResult TryEnqueue(string record);
}
