// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Coordinates the irreversible Runtime drain lifecycle.
/// </summary>
public interface IRuntimeDrainManager
{
    /// <summary>
    /// Starts, replays, or escalates the Runtime drain.
    /// </summary>
    /// <param name="drainMode">The requested drain scheduling mode.</param>
    /// <returns>The immutable first acceptance time and active drain mode.</returns>
    /// <remarks>
    /// Once selection or scheduling fails, subsequent requests rethrow the original failure without
    /// reopening admission or retrying potentially partially selected workers.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="drainMode"/> is unsupported.</exception>
    /// <exception cref="RuntimeDrainConflictException">
    /// Thrown when a Phased request attempts to downgrade an active Immediate drain.
    /// </exception>
    RuntimeDrainReceipt Accept(RuntimeDrainMode drainMode);
}
