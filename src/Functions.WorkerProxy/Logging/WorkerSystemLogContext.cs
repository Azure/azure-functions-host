// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed record WorkerSystemLogContext
{
    public string? WorkerId { get; init; }

    public long? ProxySessionId { get; init; }

    public string? FunctionAppName { get; init; }

    internal WorkerSystemLogContext Merge(WorkerSystemLogContext inner) => new()
    {
        WorkerId = inner.WorkerId ?? WorkerId,
        ProxySessionId = inner.ProxySessionId ?? ProxySessionId,
        FunctionAppName = inner.FunctionAppName ?? FunctionAppName
    };
}
