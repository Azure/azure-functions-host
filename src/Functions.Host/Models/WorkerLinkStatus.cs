// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// The outcome of a worker link attempt.
/// </summary>
public enum WorkerLinkStatus
{
    /// <summary>
    /// The worker is linked: connection, StartStream, init handshake, and channel registration completed.
    /// </summary>
    Linked,

    /// <summary>
    /// Admission was rejected or the connection or initialization handshake failed.
    /// </summary>
    LinkFailed,
}
