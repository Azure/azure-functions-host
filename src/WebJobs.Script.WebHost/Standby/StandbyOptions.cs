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
    }
}
