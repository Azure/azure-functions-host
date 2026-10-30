// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed record WorkerSystemLogEvent
{
    public int SchemaVersion => 2;

    public DateTimeOffset EventTimestamp { get; init; }

    public required string Severity { get; init; }

    public required string EventName { get; init; }

    public required string Category { get; init; }

    public string OriginComponent => "WorkerProxy";

    public string EmitterComponent => "WorkerProxy";

    public required string EmitterInstanceId { get; init; }

    public long Sequence { get; init; }

    public string? WorkerId { get; init; }

    public string? WorkerIdHash { get; init; }

    public string? LinkId { get; init; }

    public long? ProxySessionId { get; init; }

    public string? InvocationId { get; init; }

    public string? InvocationIdHash { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string? Operation { get; init; }

    public string? Stage { get; init; }

    public string? Outcome { get; init; }

    public string? ReasonCode { get; init; }

    public double? DurationMs { get; init; }

    public WorkerSystemLogAttributes? Attributes { get; init; }

    public required string Message { get; init; }

    public string? ExceptionType { get; init; }

    public bool Truncated { get; init; }

    public bool InvalidLinkId { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkerSystemLogEvent))]
internal partial class WorkerSystemLogJsonContext : JsonSerializerContext;
