// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal class FunctionMetadataProvider : IFunctionMetadataProvider
    {
        private readonly IEnvironment _environment;
        private readonly ILogger<FunctionMetadataProvider> _logger;
        private IWorkerFunctionMetadataProvider _workerFunctionMetadataProvider;
        private IHostFunctionMetadataProvider _hostFunctionMetadataProvider;

        public FunctionMetadataProvider(ILogger<FunctionMetadataProvider> logger, IWorkerFunctionMetadataProvider workerFunctionMetadataProvider, IHostFunctionMetadataProvider hostFunctionMetadataProvider)
        {
            _logger = logger;
            _workerFunctionMetadataProvider = workerFunctionMetadataProvider;
            _hostFunctionMetadataProvider = hostFunctionMetadataProvider;
            _environment = SystemEnvironment.Instance;
        }

        public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; private set; }

        public async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, IEnvironment environment, bool forceRefresh = false)
        {
            bool workerIndexing = Utility.CanWorkerIndex(workerConfigs, _environment);
            if (!workerIndexing)
            {
                return await GetMetadataFromHostProvider(workerConfigs, environment, forceRefresh);
            }

            _logger.LogInformation("Worker indexing is enabled");

            FunctionMetadataResult functionMetadataResult = await _workerFunctionMetadataProvider?.GetFunctionMetadataAsync(workerConfigs, SystemEnvironment.Instance, forceRefresh);
            FunctionErrors = _workerFunctionMetadataProvider.FunctionErrors;

            if (functionMetadataResult.UseDefaultMetadataIndexing)
            {
                _logger.LogDebug("Fallback to host indexing as worker denied indexing");
                return await GetMetadataFromHostProvider(workerConfigs, environment, forceRefresh);
            }

            return functionMetadataResult.Functions;
        }

        private async Task<ImmutableArray<FunctionMetadata>> GetMetadataFromHostProvider(IEnumerable<RpcWorkerConfig> workerConfigs, IEnvironment environment, bool forceRefresh = false)
        {
            var functions = await _hostFunctionMetadataProvider?.GetFunctionMetadataAsync(workerConfigs, environment, forceRefresh);
            FunctionErrors = _hostFunctionMetadataProvider.FunctionErrors;
            return functions;
        }
    }
}