// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Defines the stable error codes returned by the management APIs.
/// </summary>
internal static class WorkerApiErrorCodes
{
    // Clients branch on these exact, case-sensitive wire values. Do not change or repurpose them.
    // Keep explicit literals rather than nameof so symbol renames cannot change the contract.
    public const string Required = "Required";
    public const string InvalidBody = "InvalidBody";
    public const string InvalidValue = "InvalidValue";
    public const string InvalidRevision = "InvalidRevision";
    public const string WorkerNotReady = "WorkerNotReady";
    public const string WorkerTerminated = "WorkerTerminated";
    public const string AssignmentConflict = "AssignmentConflict";
}
