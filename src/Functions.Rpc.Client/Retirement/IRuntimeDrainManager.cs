// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Defines the supported Runtime drain scheduling modes.
/// </summary>
public enum RuntimeDrainMode
{
    /// <summary>
    /// Selects non-HTTP Workers in fixed batches and HTTP Workers at the fixed cutoff.
    /// </summary>
    Phased,

    /// <summary>
    /// Selects every linked Worker immediately.
    /// </summary>
    Immediate,
}

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

internal interface IRuntimeDrainWorkerManager
{
    RuntimeDrainStart CloseAdmission();

    void SelectWorkers(IReadOnlyList<string> workerIds);
}

/// <summary>
/// Confirms the active drain mode and immutable first acceptance time.
/// </summary>
/// <param name="DrainMode">The active drain mode.</param>
/// <param name="AcceptedAtUtc">The first acceptance time.</param>
public sealed record RuntimeDrainReceipt(
    RuntimeDrainMode DrainMode,
    DateTimeOffset AcceptedAtUtc);

internal sealed record RuntimeDrainStart(
    IReadOnlyList<string> NonHttpWorkerIds,
    IReadOnlyList<string> HttpWorkerIds);

/// <summary>
/// Reports an incompatible retry of an irreversible Runtime drain.
/// </summary>
public sealed class RuntimeDrainConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeDrainConflictException"/> class.
    /// </summary>
    /// <param name="message">The conflict description.</param>
    public RuntimeDrainConflictException(string message)
        : base(message)
    {
    }
}
