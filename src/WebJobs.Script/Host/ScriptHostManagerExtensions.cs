// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptHostManagerExtensions
    {
        public static async Task<bool> DelayUntilHostReady(this IScriptHostManager hostManager, int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds)
        {
            bool CanInvoke()
            {
                return hostManager.State == ScriptHostState.Running || hostManager.State == ScriptHostState.Initialized;
            }

            await Utility.DelayAsync(timeoutSeconds, pollingIntervalMilliseconds, () =>
            {
                return !CanInvoke() &&
                        hostManager.State != ScriptHostState.Error;
            });

            bool hostReady = CanInvoke();

            return hostReady;
        }
    }
}
