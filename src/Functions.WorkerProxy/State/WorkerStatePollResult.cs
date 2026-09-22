// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Represents the outcome of waiting for a worker-pod state change.
/// </summary>
/// <remarks>
/// State contains the updated snapshot when a change is observed, or is null when the wait times out without a change.
/// </remarks>
internal sealed record WorkerStatePollResult(WorkerPodState? State)
{
    public static WorkerStatePollResult NoChange { get; } = new(State: null);

    public bool HasChanged => State is not null;
}
