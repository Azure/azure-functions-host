// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Identifies an admission or initialization failure without depending on HTTP status codes.
/// </summary>
public enum WorkerLinkFailureReason
{
    /// <summary>A worker identifier is already associated with a different endpoint.</summary>
    Conflict,

    /// <summary>The worker's previously initialized channel has terminated.</summary>
    WorkerTerminated,

    /// <summary>The registry is shutting down.</summary>
    RuntimeStopping,

    /// <summary>The transport or worker initialization handshake was unavailable.</summary>
    Unavailable,
}
