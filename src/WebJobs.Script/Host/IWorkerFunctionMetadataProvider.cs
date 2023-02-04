// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Defines an interface for fetching function metadata from Out-of-Proc language workers
    /// </summary>
    internal interface IWorkerFunctionMetadataProvider
    {
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        /// <summary>
        /// Attempts to get function metadata from Out-of-Proc language workers
        /// </summary>
        /// <returns>FunctionMetadataResult that either contains the function metadata or indicates that a fall back option for fetching metadata should be used</returns>
        Task<FunctionMetadataResult> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh = false);
    }
}