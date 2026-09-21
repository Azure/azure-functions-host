// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Reports an expected worker link failure to the compute HTTP adapter.
/// </summary>
public sealed class WorkerLinkException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerLinkException"/> class.
    /// </summary>
    /// <param name="reason">The admission or initialization failure.</param>
    /// <param name="message">A safe description for the caller.</param>
    /// <param name="innerException">The underlying failure for diagnostics.</param>
    public WorkerLinkException(WorkerLinkFailureReason reason, string message, Exception innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>Gets the reason the worker could not be linked.</summary>
    public WorkerLinkFailureReason Reason { get; }
}
