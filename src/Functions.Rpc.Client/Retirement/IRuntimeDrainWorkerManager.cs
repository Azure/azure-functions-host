// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Provides admission fencing and worker selection for process-local Runtime drain.
/// </summary>
internal interface IRuntimeDrainWorkerManager
{
    /// <summary>
    /// Permanently closes new-link admission and snapshots registered, non-terminal workers, including those still
    /// preparing their extension payload.
    /// </summary>
    /// <returns>The original frozen snapshot, including on subsequent calls after workers unlink.</returns>
    RuntimeDrainStart CloseAdmission();

    /// <summary>
    /// Stops invocation admission and withdraws capacity for selected workers, then starts their
    /// shared drain and unlink lifecycle with the Runtime drain deadline.
    /// </summary>
    /// <param name="workerIds">Worker IDs from the frozen snapshot that have not yet been selected by this Runtime drain.</param>
    /// <remarks>
    /// Admission must already be closed. Missing workers and terminal channels without an existing drain are skipped.
    /// Existing scale-in or idle-retirement drains retain their cleanup owner and gain the Runtime deadline.
    /// This method starts background work without waiting for invocations or unlink to finish.
    /// </remarks>
    void SelectWorkers(IReadOnlyList<string> workerIds);
}
