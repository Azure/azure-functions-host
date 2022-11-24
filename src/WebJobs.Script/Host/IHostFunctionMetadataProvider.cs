// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Defines an interface for fetching function metadata from function.json files
    /// </summary>
    internal interface IHostFunctionMetadataProvider
    {
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        /// <summary>
        /// Reads function metadata from function.json files present along with each function
        /// </summary>
        Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, IEnvironment environment, bool forceRefresh = false);
    }
}