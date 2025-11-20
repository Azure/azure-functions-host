// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Note: Only adding the bare minimum values for refactoring purposes.
/// </summary>
public enum WorkerManagerState
{
    /// <summary>
    /// The WorkerManager has not yet been initialized.
    /// </summary>
    Default,

    /// <summary>
    /// The WorkerManager has been initialized.
    /// </summary>
    Initialized
}
