// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed record WorkerSystemLogContext
{
    public string? WorkerId { get; init; }

    public long? ProxySessionId { get; init; }

    public string? PodName { get; init; }

    public string? FunctionAppName { get; init; }

    public string? FunctionGroupName { get; init; }

    public long? Revision { get; init; }

    internal static WorkerSystemLogContext FromState(WorkerPodState state) => new()
    {
        WorkerId = state.WorkerId,
        ProxySessionId = state.SessionId,
        PodName = state.PodName,
        FunctionAppName = state.FunctionAppName,
        FunctionGroupName = state.FunctionGroupName,
        Revision = state.Revision
    };

    internal WorkerSystemLogContext Merge(WorkerSystemLogContext inner)
    {
        bool differentSession = ProxySessionId != inner.ProxySessionId
            && (ProxySessionId.HasValue || Revision.HasValue)
            && (inner.ProxySessionId.HasValue || inner.Revision.HasValue);
        // A full snapshot with no session must not inherit an outer scope's identity.
        if (differentSession || (inner.Revision.HasValue && inner.ProxySessionId is null))
        {
            return inner;
        }

        return new()
        {
            WorkerId = inner.WorkerId ?? WorkerId,
            ProxySessionId = inner.ProxySessionId ?? ProxySessionId,
            PodName = inner.PodName ?? PodName,
            FunctionAppName = inner.FunctionAppName ?? FunctionAppName,
            FunctionGroupName = inner.FunctionGroupName ?? FunctionGroupName,
            Revision = inner.Revision ?? Revision
        };
    }
}
