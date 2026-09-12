// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;
using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Reports worker-pod eligibility and known assignment identity, not runtime serving readiness.
/// </summary>
internal sealed record WorkerPodStateResponse(
    [property: JsonConverter(typeof(JsonStringEnumConverter<WorkerPodStatus>))] WorkerPodStatus PodStatus,
    string? FunctionGroupName,
    bool? IsAlwaysReady);
