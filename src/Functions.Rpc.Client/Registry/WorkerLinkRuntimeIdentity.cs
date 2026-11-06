// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Identifies the Runtime pod linking to a WorkerProxy.
/// </summary>
/// <remarks>
/// Only the declared identity fields participate in transport and ordinal value-based retry matching.
/// Unknown JSON properties are ignored. WorkerProxy owns semantic validation of these fields.
/// </remarks>
/// <param name="ResourceNamespace">The namespace containing the Runtime pod.</param>
/// <param name="PodName">The Runtime pod name.</param>
/// <param name="App">The function app associated with the Runtime pod.</param>
public sealed record WorkerLinkRuntimeIdentity(string? ResourceNamespace, string? PodName, WorkerLinkFunctionAppIdentity? App);
