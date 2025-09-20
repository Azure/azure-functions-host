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
            Task.FromResult(HealthCheckResult.Unhealthy("No script host available"));

        private static readonly Task<HealthCheckResult> _unhealthyNotStarted =
            Task.FromResult(HealthCheckResult.Unhealthy("Script host not started"));

        private static readonly Task<HealthCheckResult> _unhealthyStopping =
            Task.FromResult(HealthCheckResult.Unhealthy("Script host stopping"));

        private static readonly Task<HealthCheckResult> _unhealthyStopped =
            Task.FromResult(HealthCheckResult.Unhealthy("Script host stopped"));

        private static readonly Task<HealthCheckResult> _unhealthyOffline =
            Task.FromResult(HealthCheckResult.Unhealthy("Script host offline"));

        private static readonly Task<HealthCheckResult> _unhealthyUnknown =
            Task.FromResult(HealthCheckResult.Unhealthy("Script host in unknown state"));

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
            => Task.FromResult(HealthCheckResult.Unhealthy($"Script host in error state: {Environment.NewLine}{ex.Message}", ex));
    }
}
