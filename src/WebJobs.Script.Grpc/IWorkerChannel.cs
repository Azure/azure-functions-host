// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Represents a worker communication channel.
    /// </summary>
    internal interface IWorkerChannel
    {
        /// <summary>
        /// Gets the worker identifier.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets the current worker status.
        /// </summary>
        /// <returns>A task that produces the current worker status.</returns>
        Task<WorkerStatus> GetWorkerStatusAsync();
    }
}