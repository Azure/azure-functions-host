// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Grpc.Core;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Provides adapters for gRPC duplex calls.
/// </summary>
internal static class GrpcDuplexStreamExtensions
{
    /// <summary>
    /// Wraps a gRPC duplex call as a bidirectional channel.
    /// </summary>
    /// <typeparam name="T">The message type used in both directions.</typeparam>
    /// <param name="call">The gRPC duplex call.</param>
    /// <param name="callLifetimeSource">An optional cancellation source whose ownership transfers to the stream.</param>
    /// <param name="ownedResource">An optional transport resource whose ownership transfers to the stream.</param>
    /// <returns>The channel-backed duplex stream.</returns>
    internal static GrpcDuplexStream<T> AsDuplexStream<T>(this AsyncDuplexStreamingCall<T, T> call,
        CancellationTokenSource callLifetimeSource = null, IDisposable ownedResource = null)
        where T : class
    {
        return new GrpcDuplexStream<T>(call, callLifetimeSource, ownedResource);
    }
}
