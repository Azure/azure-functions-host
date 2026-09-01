// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Identifies one peer in a FunctionRpc relay session.
/// </summary>
internal enum FunctionRpcRelaySide
{
    /// <summary>
    /// The Host Rpc.Client-facing stream.
    /// </summary>
    Runtime,

    /// <summary>
    /// The existing language worker-facing stream.
    /// </summary>
    Worker
}
