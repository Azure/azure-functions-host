// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script;

/// <summary>
/// Provides state from the active Host assignment.
/// </summary>
public interface IHostAssignmentState
{
    /// <summary>
    /// Gets a value indicating whether the Host is assigned for trigger discovery.
    /// </summary>
    bool IsDiscoveryPod { get; }
}
