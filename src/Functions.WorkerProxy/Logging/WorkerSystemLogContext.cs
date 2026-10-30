// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed record WorkerSystemLogContext
{
    public string? WorkerId { get; init; }

    public string? LinkId { get; init; }

    public long? ProxySessionId { get; init; }

    public string? InvocationId { get; init; }

    public string? Operation { get; init; }

    public string? Stage { get; init; }

    public string? Outcome { get; init; }

    public string? ReasonCode { get; init; }

    public double? DurationMs { get; init; }

    public WorkerSystemLogAttributes? Attributes { get; init; }

    internal WorkerSystemLogContext Merge(WorkerSystemLogContext inner) => new()
    {
        WorkerId = inner.WorkerId ?? WorkerId,
        LinkId = inner.LinkId ?? LinkId,
        ProxySessionId = inner.ProxySessionId ?? ProxySessionId,
        InvocationId = inner.InvocationId ?? InvocationId,
        Operation = inner.Operation ?? Operation,
        Stage = inner.Stage ?? Stage,
        Outcome = inner.Outcome ?? Outcome,
        ReasonCode = inner.ReasonCode ?? ReasonCode,
        DurationMs = inner.DurationMs ?? DurationMs,
        Attributes = inner.Attributes ?? Attributes
    };
}

internal sealed record WorkerSystemLogAttributes
{
    public int? HttpStatusCode { get; init; }

    public string? ForwarderError { get; init; }

    public string? RpcStatus { get; init; }

    public string? PeerSide { get; init; }

    public long? StateRevision { get; init; }

    public bool? WorkerReady { get; init; }

    public string? AssignmentState { get; init; }
}
