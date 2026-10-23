// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Represents an immutable snapshot of linked worker counts and total HTTP request capacity.
/// </summary>
/// <param name="CreatedTime">The UTC time when the counts or capacity last changed.</param>
/// <param name="HttpCapacity">
/// The sum of registry-ready HTTP workers' assigned concurrency limits, not remaining capacity after active requests.
/// Capacity is zero once the root Host begins stopping; it does not indicate ScriptHost availability.
/// </param>
/// <param name="WorkerCount">The number of initialized, linked workers, independently of invocation readiness.</param>
/// <param name="HttpWorkerCount">The number of initialized, linked HTTP-group workers, independently of invocation readiness.</param>
public sealed record RuntimeState(DateTimeOffset CreatedTime, long HttpCapacity, int WorkerCount, int HttpWorkerCount);
