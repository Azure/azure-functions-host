// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models;

/// <summary>
/// Represents assignment state supplied by the Functions API server.
/// </summary>
public sealed class ApiServerAssignmentContext
{
    /// <summary>
    /// Gets or sets a value indicating whether the Host is assigned for trigger discovery.
    /// </summary>
    [JsonProperty("isDiscoveryPod")]
    public bool IsDiscoveryPod { get; set; }
}
