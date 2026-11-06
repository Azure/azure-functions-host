// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed record WorkerSystemLogContext
{
    public string? WorkerId { get; init; }

    public long? ProxySessionId { get; init; }

    public WorkerPodState? State { get; init; }

    public string? RequestId { get; init; }

    public string? InvocationId { get; init; }

    public string? FunctionId { get; init; }

    public string? BuildVersion { get; init; }

    public string? Operation { get; init; }

    public string? Phase { get; init; }

    public string? Outcome { get; init; }

    public string? ReasonCode { get; init; }

    public string? Side { get; init; }

    public int? StatusCode { get; init; }

    public string? RpcStatus { get; init; }

    public string? ForwarderError { get; init; }

    public double? ElapsedMs { get; init; }

    public string? CancellationSource { get; init; }

    public int? Attempts { get; init; }

    public bool? ResponseStarted { get; init; }

    public long? EventRevision { get; init; }

    public int? FunctionCount { get; init; }

    public int? UnsuccessfulCount { get; init; }

    public int? PendingMessages { get; init; }

    internal static WorkerSystemLogContext FromState(WorkerPodState state) => new()
    {
        State = state,
        WorkerId = state.WorkerId,
        ProxySessionId = state.SessionId
    };

    internal WorkerSystemLogContext Merge(WorkerSystemLogContext inner) => inner.State is not null ? inner : new()
    {
        WorkerId = inner.WorkerId ?? WorkerId,
        ProxySessionId = inner.ProxySessionId ?? ProxySessionId,
        State = State,
        RequestId = inner.RequestId ?? RequestId,
        InvocationId = inner.InvocationId ?? InvocationId,
        FunctionId = inner.FunctionId ?? FunctionId,
        BuildVersion = inner.BuildVersion ?? BuildVersion,
        Operation = inner.Operation ?? Operation,
        Phase = inner.Phase ?? Phase,
        Outcome = inner.Outcome ?? Outcome,
        ReasonCode = inner.ReasonCode ?? ReasonCode,
        Side = inner.Side ?? Side,
        StatusCode = inner.StatusCode ?? StatusCode,
        RpcStatus = inner.RpcStatus ?? RpcStatus,
        ForwarderError = inner.ForwarderError ?? ForwarderError,
        ElapsedMs = inner.ElapsedMs ?? ElapsedMs,
        CancellationSource = inner.CancellationSource ?? CancellationSource,
        Attempts = inner.Attempts ?? Attempts,
        ResponseStarted = inner.ResponseStarted ?? ResponseStarted,
        EventRevision = inner.EventRevision ?? EventRevision,
        FunctionCount = inner.FunctionCount ?? FunctionCount,
        UnsuccessfulCount = inner.UnsuccessfulCount ?? UnsuccessfulCount,
        PendingMessages = inner.PendingMessages ?? PendingMessages
    };
}
