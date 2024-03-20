// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptHostManagerExtensions
    {
        public static async Task<bool> DelayUntilHostReady(this IScriptHostManager hostManager, int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds)
        {
            if (HostIsInitialized(hostManager))
            {
                return true;
            }
            else
            {
                await Utility.DelayAsync(timeoutSeconds, pollingIntervalMilliseconds, () =>
                {
                    return !HostIsInitialized(hostManager) && hostManager.State != ScriptHostState.Error;
                });

                return HostIsInitialized(hostManager);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the host in an initialized or running state.
        /// </summary>
        /// <param name="hostManager">The <see cref="IScriptHostManager"/> to check.</param>
        /// <returns>True if the host is initialized or running, false otherwise.</returns>
        public static bool HostIsInitialized(this IScriptHostManager hostManager)
        {
            return hostManager.State == ScriptHostState.Running || hostManager.State == ScriptHostState.Initialized;
        }
    }
}
