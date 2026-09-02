// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    /// <summary>
    /// Defines the shared Functions worker RPC protocol.
    /// </summary>
    internal interface IFunctionRpcChannel : IWorkerChannel
    {
        /// <summary>
        /// Gets the worker configuration.
        /// </summary>
        RpcWorkerConfig WorkerConfig { get; }

        /// <summary>
        /// Gets the invocation input buffers keyed by function identifier.
        /// </summary>
        IDictionary<string, BufferBlock<ScriptInvocationContext>> FunctionInputBuffers { get; }

        /// <summary>
        /// Gets a value indicating whether the channel can accept invocations.
        /// </summary>
        /// <returns><see langword="true"/> when the channel is ready; otherwise, <see langword="false"/>.</returns>
        bool IsChannelReadyForInvocations();

        /// <summary>
        /// Creates invocation input buffers for the supplied functions.
        /// </summary>
        /// <param name="functions">The functions that can be invoked through the channel.</param>
        void SetupFunctionInvocationBuffers(IEnumerable<FunctionMetadata> functions);

        /// <summary>
        /// Sends function load requests to the worker.
        /// </summary>
        /// <param name="managedDependencyOptions">The managed dependency configuration.</param>
        /// <param name="functionTimeout">The configured function timeout, if any.</param>
        void SendFunctionLoadRequests(ManagedDependencyOptions managedDependencyOptions, TimeSpan? functionTimeout);

        /// <summary>
        /// Requests a worker environment reload.
        /// </summary>
        /// <returns>A task that produces <see langword="true"/> when the reload succeeds.</returns>
        Task<bool> SendFunctionEnvironmentReloadRequest();

        /// <summary>
        /// Sends a worker warmup request.
        /// </summary>
        void SendWorkerWarmupRequest();

        /// <summary>
        /// Requests indexed function metadata from the worker.
        /// </summary>
        /// <returns>A task that produces the worker-provided function metadata.</returns>
        Task<List<RawFunctionMetadata>> GetFunctionMetadata();

        /// <summary>
        /// Waits for active invocations to drain.
        /// </summary>
        /// <returns>A task that completes after all active invocations finish.</returns>
        Task DrainInvocationsAsync();

        /// <summary>
        /// Gets a value indicating whether an invocation is executing.
        /// </summary>
        /// <param name="invocationId">The invocation identifier.</param>
        /// <returns><see langword="true"/> when the invocation is executing; otherwise, <see langword="false"/>.</returns>
        bool IsExecutingInvocation(string invocationId);

        /// <summary>
        /// Shuts down active channel operations.
        /// </summary>
        /// <param name="workerException">The worker failure that caused shutdown, if any.</param>
        void Shutdown(Exception workerException);
    }
}
