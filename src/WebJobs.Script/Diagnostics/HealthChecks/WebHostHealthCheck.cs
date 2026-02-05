// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// A health check that reports the status of the web host lifecycle.
    /// </summary>
    /// <param name="lifetime">The application lifetime.</param>
    internal class WebHostHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
    {
        private static readonly Task<HealthCheckResult> _healthy = Task.FromResult(HealthCheckResult.Healthy());
        private static readonly Task<HealthCheckResult> _unhealthyNotStarted = CreateUnhealthyResult("Not Started", "NotStarted");
        private static readonly Task<HealthCheckResult> _unhealthyStopping = CreateUnhealthyResult("Stopping", "Stopping");
        private static readonly Task<HealthCheckResult> _unhealthyStopped = CreateUnhealthyResult("Stopped", "Stopped");

        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            bool isStopped = lifetime.ApplicationStopped.IsCancellationRequested;
            if (isStopped)
            {
                return _unhealthyStopped;
            }

            bool isStopping = lifetime.ApplicationStopping.IsCancellationRequested;
            if (isStopping)
            {
                return _unhealthyStopping;
            }

            bool isStarted = lifetime.ApplicationStarted.IsCancellationRequested;
            if (!isStarted)
            {
                return _unhealthyNotStarted;
            }

            return _healthy;
        }

        private static Task<HealthCheckResult> CreateUnhealthyResult(string reason, string errorCode)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                reason, data: new HealthCheckData() { Area = HealthCheckData.Areas.Lifecycle, ErrorCode = errorCode }));
        }
    }
}
