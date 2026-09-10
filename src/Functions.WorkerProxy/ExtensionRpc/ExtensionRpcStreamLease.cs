// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Azure.Functions.WorkerProxy.ExtensionRpc;

/// <summary>
/// Owns the lifetime of the single physical extension RPC stream registered with the coordinator.
/// </summary>
/// <param name="owner">The coordinator that owns the stream registration.</param>
/// <param name="stream">The registered stream.</param>
internal sealed class ExtensionRpcStreamLease(ExtensionRpcStreamCoordinator owner, ExtensionRpcStream stream)
    : IAsyncDisposable
{
    /// <summary>
    /// Gets the registered extension RPC stream.
    /// </summary>
    public ExtensionRpcStream Stream => stream;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        owner.Close(Stream);

        return ValueTask.CompletedTask;
    }
}
