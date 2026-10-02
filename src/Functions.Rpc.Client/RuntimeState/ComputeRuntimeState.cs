// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Represents an immutable snapshot of linked worker counts and total HTTP request capacity.
/// </summary>
/// <param name="CreatedTime">The UTC time when this snapshot was created.</param>
/// <param name="HttpCapacity">
/// The total capacity of linked, invocation-ready workers in the HTTP function group, using a fixed concurrency of 16 per worker.
/// This is total capacity, not available capacity after accounting for active requests.
/// </param>
/// <param name="WorkerCount">The number of initialized, linked workers, including workers not yet ready for invocations.</param>
/// <param name="HttpWorkerCount">The number of initialized, linked HTTP-group workers, regardless of invocation readiness.</param>
/// <remarks>
/// Capacity is zero unless ScriptHost is running and the Host is not stopping. Worker counts reflect registry membership,
/// independently of ScriptHost state or invocation readiness.
/// </remarks>
public sealed record ComputeRuntimeState(DateTimeOffset CreatedTime, long HttpCapacity, int WorkerCount, int HttpWorkerCount);
