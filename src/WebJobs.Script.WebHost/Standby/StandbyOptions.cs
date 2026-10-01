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
        /// Gets or sets a value indicating whether the hosting model supports a script host running in placeholder mode.
        /// The default is <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// When <see langword="true"/>, specialization specializes the workers, restarts the script host, and waits for
        /// it to be ready after applying the application configuration. Set to <see langword="false"/> when the script
        /// host starts later with the specialized configuration, as in compute separation mode where it starts after
        /// the first worker links. Specialization then applies the configuration without specializing workers,
        /// restarting the script host, or waiting for it to be ready.
        /// </remarks>
        public bool SupportPlaceholderScriptHost { get; set; } = true;
    }
}
