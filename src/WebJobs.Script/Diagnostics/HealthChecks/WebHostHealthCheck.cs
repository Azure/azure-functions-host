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
        private static readonly Task<HealthCheckResult> _unhealthyNotStarted = Task.FromResult(HealthCheckResult.Unhealthy("Not Started"));
        private static readonly Task<HealthCheckResult> _unhealthyStopping = Task.FromResult(HealthCheckResult.Unhealthy("Stopping"));
        private static readonly Task<HealthCheckResult> _unhealthyStopped = Task.FromResult(HealthCheckResult.Unhealthy("Stopped"));

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
    }
}
