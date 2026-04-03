// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Tracks the connection state of an external worker.
/// Used as the API response type for worker management endpoints.
/// </summary>
public sealed class WorkerConnectionInfo
{
    /// <summary>
    /// Gets or sets the platform-assigned worker identifier.
    /// </summary>
    [JsonProperty("workerId")]
    public string WorkerId { get; set; }

    /// <summary>
    /// Gets or sets the current connection state.
    /// </summary>
    [JsonProperty("state")]
    public WorkerConnectionState State { get; set; }

    /// <summary>
    /// Gets or sets the error message when <see cref="State"/> is <see cref="WorkerConnectionState.Error"/>.
    /// Null for all other states.
    /// </summary>
    [JsonProperty("errorMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string ErrorMessage { get; set; }
}
