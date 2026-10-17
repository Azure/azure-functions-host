// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// DRAFT abstraction for the Flex Consumption Network Troubleshooter. An extension implements this to
    /// validate connectivity to its trigger dependency using its own SDK and connection resolution,
    /// without taking a dependency on <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>. The host
    /// adapts all registered validators into a single connectivity <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck"/>.
    /// </summary>
    /// <remarks>
    /// Final home is <c>Microsoft.Azure.WebJobs</c> (the WebJobs SDK), which both the host and the
    /// extensions already reference — so extensions add no new package dependency. Defined here in the
    /// host only to prototype the mechanism end-to-end.
    /// </remarks>
    internal interface IConnectivityValidator
    {
        /// <summary>Gets the trigger binding type this validator handles, e.g. <c>"eventHubTrigger"</c>.</summary>
        string TriggerType { get; }

        /// <summary>Performs a non-mutating connectivity + auth probe for a single trigger binding.</summary>
        Task<ConnectivityResult> ValidateAsync(ConnectivityContext context, CancellationToken cancellationToken);
    }

    /// <summary>The target of a connectivity probe, supplied by the host from a trigger binding.</summary>
    internal sealed class ConnectivityContext
    {
        public ConnectivityContext(string triggerType, string connection, IReadOnlyDictionary<string, object> properties)
        {
            TriggerType = triggerType;
            Connection = connection;
            Properties = properties;
        }

        /// <summary>Gets the trigger binding type, e.g. <c>"eventHubTrigger"</c>.</summary>
        public string TriggerType { get; }

        /// <summary>Gets the connection setting name from the binding; the extension resolves it.</summary>
        public string Connection { get; }

        /// <summary>Gets the raw binding settings (e.g. <c>eventHubName</c>, <c>queueName</c>); the extension reads what it needs.</summary>
        public IReadOnlyDictionary<string, object> Properties { get; }
    }

    /// <summary>The result of a connectivity probe.</summary>
    internal sealed class ConnectivityResult
    {
        private ConnectivityResult(bool isHealthy, string details)
        {
            IsHealthy = isHealthy;
            Details = details;
        }

        public bool IsHealthy { get; }

        public string Details { get; }

        public static ConnectivityResult Healthy(string details = null) => new(true, details);

        public static ConnectivityResult Unhealthy(string details) => new(false, details);
    }
}
