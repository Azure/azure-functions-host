// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Provides a stable error code and optional diagnostic detail without echoing request values.
/// </summary>
/// <param name="Code">A case-sensitive contract identifier; existing codes must not be renamed or repurposed.</param>
/// <param name="Detail">Diagnostic text that may change and must not be used for client decisions.</param>
/// <remarks>
/// WorkerTerminated (409) is terminal for the assigned session; retrying the same assignment on
/// this pod cannot recover it. AssignmentConflict (409) rejects a different assignment; do not
/// retry that request unchanged.
/// WorkerNotReady (503) means removal has been requested and assignment admission is permanently
/// closed for this pod; replacement is required rather than retrying assignment on the same pod.
/// Clients must inspect Code, not just the HTTP status, to choose retry or replacement behavior
/// and must handle unknown codes gracefully.
/// </remarks>
internal sealed record WorkerApiError(string Code, string? Detail = null);
