// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class RpcWorkerConfigFactory
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly IWorkerConfigurationResolver _workerConfigurationResolver;
        private Dictionary<string, RpcWorkerConfig> _workerDescriptionDictionary = new Dictionary<string, RpcWorkerConfig>();

        public RpcWorkerConfigFactory(IMetricsLogger metricsLogger,
                                        IWorkerConfigurationResolver workerConfigurationResolver)
        {
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _workerConfigurationResolver = workerConfigurationResolver ?? throw new ArgumentNullException(nameof(workerConfigurationResolver));
        }

        public IList<RpcWorkerConfig> GetConfigs()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.GetConfigs))
            {
                _workerDescriptionDictionary = _workerConfigurationResolver.GetWorkerConfigs();
                return _workerDescriptionDictionary.Values.ToList();
            }
        }
    }
}
