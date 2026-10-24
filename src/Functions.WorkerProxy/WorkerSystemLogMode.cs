// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Defines how Worker Proxy handles system logs received from a language worker.
/// </summary>
internal enum WorkerSystemLogMode
{
    /// <summary>
    /// Forwards system logs to the Functions Host without emitting them from Worker Proxy.
    /// </summary>
    Disabled,

    /// <summary>
    /// Emits system logs from Worker Proxy and also forwards them to the Functions Host.
    /// </summary>
    Mirror,

    /// <summary>
    /// Emits system logs from Worker Proxy and consumes them after a successful write.
    /// </summary>
    Consume
}
