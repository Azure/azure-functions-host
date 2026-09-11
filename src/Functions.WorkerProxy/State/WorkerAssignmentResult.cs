// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Describes an assignment outcome independently of its HTTP representation.
/// </summary>
internal enum WorkerAssignmentResult
{
    Success,
    WorkerNotReady,
    AssignmentConflict,
    WorkerTerminated
}
