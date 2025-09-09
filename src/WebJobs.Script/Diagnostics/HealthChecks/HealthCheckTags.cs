// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Contains tags used for health checks in the Azure Functions host.
    /// </summary>
    internal static class HealthCheckTags
    {
        private const string Prefix = "azure.functions.";

        /// <summary>
        /// The 'azure.functions.liveness' tag is used for liveness checks in the Azure Functions host.
        /// </summary>
        /// <remarks>
        /// Liveness checks are used to determine if the host is alive and responsive.
        /// </remarks>
        public const string Liveness = Prefix + "liveness";

        /// <summary>
        /// The 'azure.functions.readiness' tag is used for readiness checks in the Azure Functions host.
        /// </summary>
        /// <remarks>
        /// Readiness checks are used to determine if the host is ready to process requests.
        /// </remarks>
        public const string Readiness = Prefix + "readiness";

        /// <summary>
        /// The 'azure.functions.configuration' tag is used for configuration-related health checks in the Azure Functions host.
        /// </summary>
        /// <remarks>
        /// These are typically customer configuration related, such as configuring AzureWebJobsStorage access.
        /// </remarks>
        public const string Configuration = Prefix + "configuration";
    }
}
