// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Writes formatted worker system-log records to the platform output stream.
/// </summary>
internal interface IWorkerSystemLogWriter
{
    /// <summary>
    /// Writes one complete worker system-log record.
    /// </summary>
    /// <param name="record">The formatted record.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that represents the write.</returns>
    ValueTask WriteAsync(string record, CancellationToken cancellationToken);
}
