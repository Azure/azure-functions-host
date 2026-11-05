// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Identifies a pod and its function app in the platform's instance-state contract.
/// </summary>
/// <param name="ResourceNamespace">The namespace containing the pod.</param>
/// <param name="PodName">The pod name.</param>
/// <param name="App">The function app associated with the pod.</param>
internal sealed record PodIdentity(
    [property: JsonRequired] string ResourceNamespace,
    [property: JsonRequired] string PodName,
    [property: JsonRequired] FunctionAppIdentity App);
