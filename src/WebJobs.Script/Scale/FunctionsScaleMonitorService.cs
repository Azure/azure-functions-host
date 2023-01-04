// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public class FunctionsScaleMonitorService : ScaleMonitorService
    {
        private readonly IPrimaryHostStateProvider _primaryHostStateProvider;
        private readonly IEnvironment _environment;

        public FunctionsScaleMonitorService(ScaleManager scaleManager, IScaleMetricsRepository metricsRepository, ILoggerFactory loggerFactory,
            IPrimaryHostStateProvider primaryHostStateProvider, IEnvironment environment, IOptions<ScaleOptions> scaleOptions) : base(scaleManager, metricsRepository, scaleOptions, loggerFactory)
        {
            _primaryHostStateProvider = primaryHostStateProvider;
            _environment = environment;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            if (_environment.IsRuntimeScaleMonitoringEnabled())
            {
                base.StartAsync(cancellationToken);
            }

            return Task.CompletedTask;
        }

        protected override void OnTimer(object state)
        {
            if (_primaryHostStateProvider.IsPrimary)
            {
                base.OnTimer(state);
            }
        }
    }
}