// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Describes a worker link failure using a stable, case-sensitive code.
/// </summary>
/// <param name="Code">The machine-readable error code. Clients must not depend on diagnostic text.</param>
/// <param name="Detail">An optional safe diagnostic description.</param>
public sealed record WorkerLinkError(
    [property: JsonProperty("code")] string Code,
    [property: JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)] string? Detail = null);
