// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Metrics;

/// <summary>
/// Tracks worker lifetime and selected-channel attribution for compute-separated invocations.
/// </summary>
public interface IWorkerInvocationTracker
{
    /// <summary>
    /// Starts the metered lifetime of an initialized worker.
    /// </summary>
    /// <param name="workerId">The worker pod name.</param>
    void WorkerLinked(string workerId);

    /// <summary>
    /// Ends a worker's metered lifetime and finalizes its outstanding invocations.
    /// </summary>
    /// <param name="workerId">The worker pod name.</param>
    void WorkerUnlinked(string workerId);

    /// <summary>
    /// Associates an invocation with its selected worker and atomically posts it for dispatch.
    /// </summary>
    /// <param name="invocationId">The invocation identifier.</param>
    /// <param name="workerId">The selected worker pod name.</param>
    /// <param name="cancellationToken">Checked before dispatch only; cancellation is not a completion signal.</param>
    /// <param name="dispatch">Posts the invocation to the selected worker's input buffer.</param>
    /// <returns>Whether the live worker accepted the invocation.</returns>
    bool TryDispatchInvocation(string invocationId, string workerId, CancellationToken cancellationToken, Func<bool> dispatch);

    /// <summary>
    /// Finalizes accounting when the channel's invocation result completes, not when cancellation is requested.
    /// </summary>
    /// <param name="invocationId">The invocation identifier.</param>
    /// <param name="success">Whether the channel returned a successful result.</param>
    void InvocationCompleted(string invocationId, bool success);
}
