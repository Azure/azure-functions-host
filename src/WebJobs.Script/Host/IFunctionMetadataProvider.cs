// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IFunctionMetadataProvider
    {
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, IEnvironment environment, bool forceRefresh = false);
    }
}