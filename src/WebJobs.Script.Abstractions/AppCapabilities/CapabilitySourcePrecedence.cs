// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    /// <summary>
    /// Defines precedence values for known capability sources.
    /// Higher values indicate higher precedence.
    /// TODO: FINALIZE ORDER OF PRECEDENCE - THIS WAS JUST RANDOMLY DONE FOR POC
    /// </summary>
    public static class CapabilitySourcePrecedence
    {
        /// <summary>
        /// Precedence for capabilities from configuration (e.g., appsettings, environment variables).
        /// Lowest precedence - can be overridden by all other sources.
        /// </summary>
        public const int Config = 10;

        /// <summary>
        /// Precedence for capabilities from the host.
        /// Overrides config, but can be overridden by worker and extension sources.
        /// </summary>
        public const int Host = 20;

        /// <summary>
        /// Precedence for capabilities from worker processes.
        /// Overrides config and host, but can be overridden by extensions.
        /// </summary>
        public const int Worker = 30;

        /// <summary>
        /// Precedence for capabilities from extensions.
        /// Highest precedence - overrides all other sources.
        /// </summary>
        public const int Extension = 40;
    }
}