// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Defines the supported BYOC Runtime drain scheduling models.
/// </summary>
public enum ByocDrainModel
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
/// Coordinates the irreversible BYOC Runtime drain lifecycle.
/// </summary>
public interface IByocRuntimeDrainManager
{
    /// <summary>
    /// Starts, replays, or escalates the Runtime drain.
    /// </summary>
    /// <param name="drainModel">The requested drain scheduling model.</param>
    /// <returns>The immutable first acceptance time and active drain model.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="drainModel"/> is unsupported.</exception>
    /// <exception cref="ByocRuntimeDrainConflictException">
    /// Thrown when a Phased request attempts to downgrade an active Immediate drain.
    /// </exception>
    ByocRuntimeDrainReceipt Accept(ByocDrainModel drainModel);
}

internal interface IByocRuntimeDrainWorkerManager
{
    ByocRuntimeDrainStart CloseAdmission();

    ByocRuntimeDrainSelection SelectWorkers(IReadOnlyList<string> workerIds);
}

/// <summary>
/// Confirms the active drain model and immutable first acceptance time.
/// </summary>
/// <param name="DrainModel">The active drain model.</param>
/// <param name="AcceptedAtUtc">The first acceptance time.</param>
public sealed record ByocRuntimeDrainReceipt(
    ByocDrainModel DrainModel,
    DateTimeOffset AcceptedAtUtc);

internal sealed record ByocRuntimeDrainStart(
    IReadOnlyList<string> NonHttpWorkerIds,
    IReadOnlyList<string> HttpWorkerIds);

internal sealed record ByocRuntimeDrainSelection(
    IReadOnlyList<string> WorkerIds,
    IReadOnlyList<Task<bool>> DrainTasks);

/// <summary>
/// Reports an incompatible retry of an irreversible BYOC Runtime drain.
/// </summary>
public sealed class ByocRuntimeDrainConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByocRuntimeDrainConflictException"/> class.
    /// </summary>
    /// <param name="message">The conflict description.</param>
    public ByocRuntimeDrainConflictException(string message)
        : base(message)
    {
    }
}
