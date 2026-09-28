// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Represents one versioned snapshot of the compute Host's total HTTP request capacity.
/// </summary>
/// <param name="SnapshotVersion">
/// A value that strictly increases with each new snapshot. It tracks the machine's monotonic clock in milliseconds, so
/// it keeps increasing across Host process restarts on the same machine, and wall-clock changes do not affect it.
/// </param>
/// <param name="HttpCapacity">
/// The sum of the advertised maximum concurrency of linked, invocation-ready workers in the HTTP function group.
/// This is total capacity, not available capacity after accounting for active requests or leases.
/// </param>
/// <remarks>Capacity is zero unless ScriptHost is running and the Host is not stopping.</remarks>
public sealed record ComputeRuntimeState(long SnapshotVersion, long HttpCapacity);
