// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Rpc;

/// <summary>
/// Identifies why a relay session terminated.
/// </summary>
internal enum FunctionRpcRelayTerminationReason
{
    /// <summary>
    /// A peer completed its request stream.
    /// </summary>
    PeerClosed,

    /// <summary>
    /// A peer canceled its gRPC call.
    /// </summary>
    Canceled,

    /// <summary>
    /// A transport read or write operation faulted.
    /// </summary>
    Faulted,

    /// <summary>
    /// The WorkerProxy application requested shutdown.
    /// </summary>
    Shutdown
}
