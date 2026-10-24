// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// SDK-free connectivity check that verifies DNS resolution for a configured set of hosts.
    /// </summary>
    /// <remarks>
    /// Prototype for the Network Troubleshooter (DRAFT). Hosts are read from the
    /// <c>NETWORK_CHECK_DNS_HOSTS</c> setting (comma-separated); the production check will derive
    /// targets from trigger binding metadata. Registered on the WebHost scope so it runs in
    /// validation mode as well as on a normal worker.
    /// </remarks>
    internal sealed class DnsConnectivityHealthCheck : IHealthCheck
    {
        private const string DnsHostsSetting = "NETWORK_CHECK_DNS_HOSTS";

        private readonly IConfiguration _configuration;

        public DnsConnectivityHealthCheck(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            string hostsSetting = _configuration[DnsHostsSetting];
            if (string.IsNullOrWhiteSpace(hostsSetting))
            {
                return HealthCheckResult.Healthy("No DNS hosts configured; connectivity check skipped.");
            }

            foreach (string host in hostsSetting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    await Dns.GetHostEntryAsync(host, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    HealthCheckData data = new() { Area = HealthCheckData.Areas.Connectivity };
                    data.SetExceptionDetails(ex);
                    return HealthCheckResult.Unhealthy($"DNS resolution failed for '{host}'.", ex, data);
                }
            }

            return HealthCheckResult.Healthy();
        }
    }
}
