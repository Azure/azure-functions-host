// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Describes the internal lifecycle of a single worker assignment.
/// </summary>
internal enum WorkerAssignmentState
{
    Unassigned,
    // The manager publishes assignment and readiness atomically, without exposing this intermediate state.
    Assigned,
    Ready,
    Failed
}
