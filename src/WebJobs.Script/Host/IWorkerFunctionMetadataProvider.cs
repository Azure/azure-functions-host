// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Defines an interface for fetching function metadata from Out-of-Proc language workers.
    /// </summary>
    public interface IWorkerFunctionMetadataProvider
    {
        /// <summary>
        /// Gets function-indexing errors keyed by function name.
        /// </summary>
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        /// <summary>
        /// Attempts to get function metadata from Out-of-Proc language workers.
        /// </summary>
        /// <param name="workerConfigs">The available worker configurations.</param>
        /// <param name="forceRefresh">Whether to bypass the provider cache.</param>
        /// <returns>A result containing worker metadata or requesting fallback to host indexing.</returns>
        Task<FunctionMetadataResult> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh = false);
    }
}