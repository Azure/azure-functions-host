// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Represents one versioned snapshot of the compute Host's linked worker counts.
/// </summary>
/// <param name="SnapshotVersion">
/// A value that strictly increases with each new snapshot. It is seeded from the clock so it keeps increasing across
/// Host process restarts.
/// </param>
/// <param name="LinkedWorkerCount">The number of linked workers across all function groups.</param>
/// <param name="LinkedHttpWorkerCount">
/// The number of linked workers in the HTTP function group that are ready for invocations. It never exceeds
/// <paramref name="LinkedWorkerCount"/>.
/// </param>
/// <remarks>Both counts are zero unless ScriptHost is running and the Host is not stopping.</remarks>
public sealed record ComputeRuntimeState(long SnapshotVersion, int LinkedWorkerCount, int LinkedHttpWorkerCount);
