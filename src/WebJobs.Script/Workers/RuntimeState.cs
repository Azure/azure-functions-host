// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Immutable snapshot of runtime-wide state relevant to the App Server:
/// the cap on linked workers and request-slot accounting.
/// Published to the mesh service as <c>publish-runtime-state</c> and
/// returned by the <c>GET /admin/instance/state</c> admin API.
/// </summary>
public sealed class RuntimeState
{
    /// <summary>
    /// Gets the maximum number of workers that may be linked to this runtime.
    /// The platform will ensure this limit is not exceeded when linking workers.
    /// </summary>
    [JsonProperty("maxLinkedWorkers")]
    public int MaxLinkedWorkers { get; init; }

    /// <summary>
    /// Gets the total number of linked workers, including unhealthy or draining workers.
    /// </summary>
    [JsonProperty("linkedWorkerCount")]
    public int LinkedWorkerCount { get; init; }

    /// <summary>
    /// Gets the total request-slot capacity contributed by all linked workers
    /// (sum of each worker's reported max concurrency). This will only include
    /// capacity from workers that have completed their init handshake and are
    /// considered healthy.
    /// </summary>
    [JsonProperty("totalRequestSlots")]
    public int TotalRequestSlots { get; init; }

    /// <summary>
    /// Gets the number of request slots currently available (total minus leased).
    /// Clamped at zero; will not report negative values when outstanding leases
    /// exceed the current total (e.g. after workers drain). Returns zero if no
    /// healthy workers are linked or the runtime is stopping.
    /// </summary>
    [JsonProperty("totalAvailableRequestSlots")]
    public int TotalAvailableRequestSlots { get; init; }
}
