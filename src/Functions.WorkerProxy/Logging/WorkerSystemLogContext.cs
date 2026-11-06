// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed record WorkerSystemLogContext
{
    public string? WorkerId { get; init; }

    public long? ProxySessionId { get; init; }

    public WorkerPodState? State { get; init; }

    internal static WorkerSystemLogContext FromState(WorkerPodState state) => new()
    {
        WorkerId = state.WorkerId,
        ProxySessionId = state.SessionId,
        State = state
    };

    internal WorkerSystemLogContext Merge(WorkerSystemLogContext inner) => inner.State is not null ? inner : new()
    {
        WorkerId = inner.WorkerId ?? WorkerId,
        ProxySessionId = inner.ProxySessionId ?? ProxySessionId,
        State = State
    };
}
