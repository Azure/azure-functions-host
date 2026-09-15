// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Identifies the immutable startup mode selected by an assignment.
/// </summary>
internal enum WorkerStartupMode
{
    /// <summary>
    /// The worker starts with its final configuration.
    /// </summary>
    Preconfigured,

    /// <summary>
    /// The worker requires configuration through specialization.
    /// </summary>
    SpecializationRequired
}
