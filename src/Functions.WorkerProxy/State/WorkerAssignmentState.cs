// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Describes the internal lifecycle of a single worker assignment.
/// </summary>
internal enum WorkerAssignmentState
{
    Unassigned,
    // The assignment is accepted before the worker establishes StartStream.
    Assigned,
    Ready,
    Failed,
    // A terminal stream is not, by itself, evidence of a health failure.
    Terminated
}
