// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Describes the worker proxy's link to a runtime pod.
/// </summary>
/// <param name="State">The current link state.</param>
/// <param name="Runtime">The linked runtime identity, when available.</param>
internal sealed record WorkerLink([property: JsonRequired] WorkerLinkState State, PodIdentity? Runtime = null);
