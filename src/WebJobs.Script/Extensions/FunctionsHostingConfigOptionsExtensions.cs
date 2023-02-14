// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FunctionsHostingConfigOptionsExtensions
    {
        /// <summary>
        /// Checks whether worker concurency feature is enabled in the hosting config.
        /// </summary>
        /// <param name="hostingConfigOptions">Extends <see cref="FunctionsHostingConfigOptions">.</param>
        /// <returns></returns>
        public static bool IsFunctionsWorkerDynamicConcurrencyEnabled(this FunctionsHostingConfigOptions hostingConfigOptions)
        {
            return hostingConfigOptions.GetFeature(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled) == "1";
        }

        /// <summary>
        /// Checks whether language workers warmup feature is enabled in the hosting config.
        /// </summary>
        /// <param name="hostingConfigOptions">Extends <see cref="FunctionsHostingConfigOptions">.</param>
        /// <returns></returns>
        public static bool IsWorkerWarmupEnabled(this FunctionsHostingConfigOptions hostingConfigOptions)
        {
            return hostingConfigOptions.GetFeature(RpcWorkerConstants.WorkerWarmupEnabled) == "1";
        }

        /// <summary>
        /// Checks whether Linux Log Backoff is disabled in the hosting config.
        /// </summary>
        /// <param name="hostingConfigOptions">Extends <see cref="FunctionsHostingConfigOptions">.</param>
        /// <returns></returns>
        public static bool IsDisableLinuxAppServiceLogBackoff(this FunctionsHostingConfigOptions hostingConfigOptions)
        {
            return hostingConfigOptions.GetFeature(ScriptConstants.HostingConfigDisableLinuxAppServiceExecutionEventLogBackoff) == "1";
        }

        /// <summary>
        /// Checks whether Linux Appservice/EP Detailed Execution Event is disabled in the hosting config.
        /// </summary>
        /// <param name="hostingConfigOptions">Extends <see cref="FunctionsHostingConfigOptions">.</param>
        /// <returns></returns>
        public static bool IsDisableLinuxAppServiceExecutionDetails(this FunctionsHostingConfigOptions hostingConfigOptions)
        {
            return hostingConfigOptions.GetFeature(ScriptConstants.HostingConfigDisableLinuxAppServiceDetailedExecutionEvents) == "1";
        }

        ///// <summary>
        ///// Gets or sets a value indicating whether Linux Appservice/EP Detailed Execution Event is disabled in the hosting config.
        ///// </summary>
        //public bool DisableLinuxAppServiceExecutionDetails
        //{
        //    get
        //    {
        //        return GetFeature(ScriptConstants.HostingConfigDisableLinuxAppServiceDetailedExecutionEvents) == "1";
        //    }

        //    set
        //    {
        //        _features[ScriptConstants.HostingConfigDisableLinuxAppServiceDetailedExecutionEvents] = value ? "1" : "0";
        //    }
        //}
    }
}
