// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.Logging;

/// <summary>
/// Represents a failure to emit a worker system log.
/// </summary>
internal sealed class WorkerSystemLogEmissionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerSystemLogEmissionException"/> class.
    /// </summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying output failure.</param>
    public WorkerSystemLogEmissionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
