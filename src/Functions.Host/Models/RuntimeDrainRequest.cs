// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;
using Azure.Functions.Rpc.Client;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Requests the scheduling mode for an irreversible Runtime drain.
/// </summary>
[JsonConverter(typeof(RuntimeDrainRequestJsonConverter))]
public sealed class RuntimeDrainRequest
{
    /// <summary>
    /// Gets or sets the exact <c>Phased</c> or <c>Immediate</c> drain mode.
    /// </summary>
    [JsonPropertyName("drainMode")]
    public RuntimeDrainMode? DrainMode { get; set; }

    internal RequestValidationError? ValidationError { get; init; }
}
