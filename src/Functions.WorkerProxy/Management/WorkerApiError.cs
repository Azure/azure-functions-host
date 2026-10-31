// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Provides a stable error code and optional diagnostic detail without echoing request values.
/// </summary>
/// <param name="Code">A case-sensitive contract identifier; existing codes must not be renamed or repurposed.</param>
/// <param name="Detail">Diagnostic text that may change and must not be used for client decisions.</param>
/// <remarks>
/// WorkerNotReady (503) permits retry after readiness. WorkerTerminated and RemovalRequested (409)
/// are terminal for this pod; retrying the same assignment cannot recover it.
/// AssignmentConflict (409) rejects a different assignment; do not retry that request unchanged.
/// Clients must inspect Code to distinguish the 409 outcomes and handle unknown codes gracefully.
/// </remarks>
internal sealed record WorkerApiError(string Code, string? Detail = null);
