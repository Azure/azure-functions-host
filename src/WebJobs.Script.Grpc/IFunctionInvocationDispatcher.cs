// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Dispatches function invocations to a configured worker topology.
    /// </summary>
    public interface IFunctionInvocationDispatcher : IDisposable
    {
        /// <summary>
        /// Gets the current dispatcher state.
        /// </summary>
        FunctionInvocationDispatcherState State { get; }

        /// <summary>
        /// Gets the worker error threshold used by host monitoring.
        /// </summary>
        int ErrorEventsThreshold { get; }

        /// <summary>
        /// Dispatches an invocation to an eligible worker.
        /// </summary>
        /// <param name="invocationContext">The invocation to dispatch.</param>
        /// <returns>A task that completes when the invocation is accepted for dispatch.</returns>
        Task InvokeAsync(ScriptInvocationContext invocationContext);

        /// <summary>
        /// Initializes the dispatcher for the supplied functions.
        /// </summary>
        /// <param name="functions">The indexed functions.</param>
        /// <param name="cancellationToken">A token that cancels initialization.</param>
        /// <returns>A task that completes when dispatcher initialization finishes.</returns>
        Task InitializeAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current status of each worker.
        /// </summary>
        /// <returns>A task that produces worker status keyed by worker ID.</returns>
        Task<IDictionary<string, WorkerStatus>> GetWorkerStatusesAsync();

        /// <summary>
        /// Waits for active invocations to finish during shutdown.
        /// </summary>
        /// <returns>A task that completes when shutdown draining finishes.</returns>
        Task ShutdownAsync();

        /// <summary>
        /// Restarts the worker executing an invocation when supported by the topology.
        /// </summary>
        /// <param name="invocationId">The invocation identifier.</param>
        /// <param name="exception">The failure that prompted the restart.</param>
        /// <returns>A task that produces <see langword="true"/> when a worker was restarted.</returns>
        Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception);

        /// <summary>
        /// Starts another worker channel when supported by the topology.
        /// </summary>
        /// <returns>A task that completes when the channel-start operation finishes.</returns>
        Task StartWorkerChannel();

        /// <summary>
        /// Prevents new work before dispatcher shutdown begins.
        /// </summary>
        void PreShutdown();
    }
}
