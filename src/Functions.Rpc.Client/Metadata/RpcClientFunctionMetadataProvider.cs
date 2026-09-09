// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Uses the root Client metadata cache without falling back to local application files.
/// </summary>
internal sealed class RpcClientFunctionMetadataProvider(IWorkerFunctionMetadataProvider workerMetadataProvider) : IFunctionMetadataProvider
{
    private readonly IWorkerFunctionMetadataProvider _workerMetadataProvider =
        workerMetadataProvider ?? throw new ArgumentNullException(nameof(workerMetadataProvider));

    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors => _workerMetadataProvider.FunctionErrors;

    public async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh = false)
    {
        FunctionMetadataResult result = await _workerMetadataProvider.GetFunctionMetadataAsync(workerConfigs, forceRefresh);
        if (result.UseDefaultMetadataIndexing)
        {
            throw new InvalidOperationException("Client-backed Host requires worker-provided function metadata.");
        }

        return result.Functions;
    }
}
