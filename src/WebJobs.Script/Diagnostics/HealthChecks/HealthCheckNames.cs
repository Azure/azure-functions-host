// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Contains names used for health checks in the Azure Functions host.
    /// </summary>
    internal static class HealthCheckNames
    {
        private const string Prefix = "azure.functions.";

        /// <summary>
        /// The 'azure.functions.web_host.lifecycle' check monitors the lifecycle of the web host.
        /// </summary>
        public const string WebHostLifeCycle = Prefix + "web_host.lifecycle";

        /// <summary>
        /// The 'azure.functions.script_host.lifecycle' check monitors the lifecycle of the script host.
        /// </summary>
        public const string ScriptHostLifeCycle = Prefix + "script_host.lifecycle";

        /// <summary>
        /// The 'azure.functions.webjobs.storage' check monitors connectivity to the WebJobs storage account.
        /// </summary>
        public const string WebJobsStorage = Prefix + "webjobs.storage";
    }
}
