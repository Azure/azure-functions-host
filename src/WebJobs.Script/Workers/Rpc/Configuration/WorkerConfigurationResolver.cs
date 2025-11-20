// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// Resolves worker configurations by aggregating results from multiple <see cref="IWorkerConfigurationProvider"/> instances,
    /// ordered by their priority. Returns a dictionary of worker configurations keyed by language name.
    /// </summary>
    internal sealed class WorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly IEnumerable<IWorkerConfigurationProvider> _providers;

        public WorkerConfigurationResolver(IEnumerable<IWorkerConfigurationProvider> providers)
        {
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        }

        public IReadOnlyDictionary<string, RpcWorkerConfig> GetWorkerConfigs()
        {
            var configs = new Dictionary<string, RpcWorkerConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in _providers.OrderByDescending(p => p.Priority))
            {
                provider.PopulateWorkerConfigs(configs);
            }

            return configs;
        }
    }
}
