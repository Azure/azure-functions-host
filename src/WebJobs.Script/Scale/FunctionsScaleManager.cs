// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Manages scale monitoring operations.
    /// </summary>
    public class FunctionsScaleManager : ScaleManager
    {
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;
        private readonly IEnvironment _environment;

        public FunctionsScaleManager(
            IScaleMonitorManager monitorManager,
            ITargetScalerManager targetScalerManager,
            IScaleMetricsRepository metricsRepository,
            IConcurrencyStatusRepository concurrencyStatusRepository,
            ILoggerFactory loggerFactory,
            IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions,
            IEnvironment environment) : base(monitorManager, targetScalerManager, metricsRepository, concurrencyStatusRepository, loggerFactory)
        {
            _functionsHostingConfigOptions = functionsHostingConfigOptions;
            _environment = environment;
        }

        public FunctionsScaleManager() : base(null, null, null, null, null)
        {
        }

        public override bool IsTargetBasedScalingEnabled
        {
            get
            {
                return _environment.IsTargetBasedScalingEnabled();
            }
        }

        public override bool IsTargetBasedScalingEnabledForTrigger(ITargetScaler scaler)
        {
            string assemblyName = GetAssemblyName(scaler.GetType());
            string flag = _functionsHostingConfigOptions.Value.GetFeature(assemblyName);
            return flag == "1";
        }
    }
}
