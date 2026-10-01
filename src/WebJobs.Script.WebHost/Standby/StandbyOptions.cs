// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Provides options values related to StandbyMode. If you need to check whether
    /// the host is currently in standby mode, use an <see cref="IOptionsMonitor{StandbyOptions}"/>.
    /// The options will only be reset after the host has started and the StandbyManager specializes
    /// the host. Avoid checking Standby and Placeholder environment variables directly as they can
    /// change at any time, even during initialization.
    /// </summary>
    public class StandbyOptions
    {
        public bool InStandbyMode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether specialization specializes the workers and restarts the script host
        /// after it applies the specialized configuration. The default is <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// Set to <see langword="false"/> when the script host doesn't run in placeholder mode and starts later with the
        /// specialized configuration. For example, in compute separation mode the script host starts after the first
        /// worker links. Specialization then completes as soon as the specialized configuration is applied, rather than
        /// waiting for a script host that hasn't started.
        /// </remarks>
        public bool RestartScriptHostOnSpecialization { get; set; } = true;
    }
}
