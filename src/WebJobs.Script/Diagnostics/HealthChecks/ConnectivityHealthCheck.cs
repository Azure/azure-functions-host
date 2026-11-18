// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// DRAFT host adapter (Flex Consumption Network Troubleshooter) that surfaces extension-provided
    /// <see cref="IConnectivityValidator"/> implementations as a single connectivity health check.
    /// It enumerates the app's triggers, matches each to a registered validator by trigger type,
    /// invokes it with the binding's connection + settings, and aggregates the results.
    /// </summary>
    /// <remarks>
    /// This keeps the <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> dependency in the host (here)
    /// and out of every extension: extensions register a plain <see cref="IConnectivityValidator"/>,
    /// and this one adapter turns them into a <c>connectivity</c>-tagged <see cref="IHealthCheck"/>.
    /// </remarks>
    internal sealed class ConnectivityHealthCheck : IHealthCheck
    {
        private readonly IEnumerable<IConnectivityValidator> _validators;
        private readonly IFunctionMetadataManager _metadataManager;

        public ConnectivityHealthCheck(
            IEnumerable<IConnectivityValidator> validators,
            IFunctionMetadataManager metadataManager)
        {
            _validators = validators ?? throw new ArgumentNullException(nameof(validators));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            Dictionary<string, object> data = new();
            bool anyUnhealthy = false;

            foreach (FunctionMetadata function in _metadataManager.GetFunctionMetadata())
            {
                BindingMetadata trigger = function.Trigger;
                if (trigger is null || string.IsNullOrEmpty(trigger.Connection))
                {
                    // Not a remote trigger (e.g. HTTP/timer) or nothing to probe.
                    continue;
                }

                IConnectivityValidator validator = _validators.FirstOrDefault(
                    v => string.Equals(v.TriggerType, trigger.Type, StringComparison.OrdinalIgnoreCase));
                if (validator is null)
                {
                    // No validator shipped for this trigger's extension (bundle-versioned coverage).
                    continue;
                }

                ConnectivityContext probeContext = new(
                    trigger.Type,
                    trigger.Connection,
                    new Dictionary<string, object>(trigger.Properties));

                ConnectivityResult result = await validator
                    .ValidateAsync(probeContext, cancellationToken)
                    .ConfigureAwait(false);

                data[function.Name] = result.IsHealthy ? "Healthy" : $"Unhealthy: {result.Details}";
                anyUnhealthy |= !result.IsHealthy;
            }

            return anyUnhealthy
                ? HealthCheckResult.Unhealthy("One or more trigger dependencies are unreachable.", data: data)
                : HealthCheckResult.Healthy(data: data);
        }
    }
}
