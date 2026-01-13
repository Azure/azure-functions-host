// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// A health check that reports the status of the script host lifecycle.
    /// </summary>
    /// <param name="manager">The script host manager.</param>
    internal class ScriptHostHealthCheck(IScriptHostManager manager) : IHealthCheck
    {
        private readonly IScriptHostManager _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        private static readonly Task<HealthCheckResult> _healthy =
            Task.FromResult(HealthCheckResult.Healthy());

        private static readonly Task<HealthCheckResult> _unhealthyNoScriptHost =
            CreateUnhealthyResult("No script host available", "NoScriptHost");

        private static readonly Task<HealthCheckResult> _unhealthyNotStarted =
            CreateUnhealthyResult("Script host not started", "Starting");

        private static readonly Task<HealthCheckResult> _unhealthyStopping =
            CreateUnhealthyResult("Script host stopping", "Stopping");

        private static readonly Task<HealthCheckResult> _unhealthyStopped =
            CreateUnhealthyResult("Script host stopped", "Stopped");

        private static readonly Task<HealthCheckResult> _unhealthyOffline =
            CreateUnhealthyResult("Script host offline", "Offline");

        private static readonly Task<HealthCheckResult> _unhealthyUnknown =
            CreateUnhealthyResult("Script host in unknown state", "Unknown");

        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => _manager.State switch
            {
                ScriptHostState.Default => _unhealthyNoScriptHost,
                ScriptHostState.Starting => _unhealthyNotStarted,
                ScriptHostState.Initialized or ScriptHostState.Running => _healthy,
                ScriptHostState.Error => UnhealthyError(_manager.LastError),
                ScriptHostState.Stopping => _unhealthyStopping,
                ScriptHostState.Stopped => _unhealthyStopped,
                ScriptHostState.Offline => _unhealthyOffline,
                _ => _unhealthyUnknown,
            };

        private static Task<HealthCheckResult> UnhealthyError(Exception ex)
        {
            HealthCheckData data = new() { Area = HealthCheckData.Areas.Lifecycle, ErrorCode = "Faulted" };
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Script host in error state: {Environment.NewLine}{ex.Message}", ex, data));
        }

        private static Task<HealthCheckResult> CreateUnhealthyResult(string reason, string errorCode)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                reason, data: new HealthCheckData() { Area = HealthCheckData.Areas.Lifecycle, ErrorCode = errorCode }));
        }
    }
}
