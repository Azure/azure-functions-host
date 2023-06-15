// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class FunctionsHostingConfigOptions
    {
        private readonly Dictionary<string, string> _features;

        public FunctionsHostingConfigOptions()
        {
            _features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets all features in the hosting configuration.
        /// </summary>
        public Dictionary<string, string> Features => _features;

        /// <summary>
        /// Gets a value indicating whether worker concurency feature is enabled in the hosting config.
        /// </summary>
        public bool FunctionsWorkerDynamicConcurrencyEnabled
        {
            get
            {
                return GetFeature(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled) == "1";
            }
        }

        /// <summary>
        /// Gets a value indicating whether worker indexing feature is enabled in the hosting config.
        /// </summary>
        public bool WorkerIndexingEnabled
        {
            get
            {
                return GetFeature(RpcWorkerConstants.WorkerIndexingEnabled) == "1";
            }
        }

        /// <summary>
        /// Gets a string delimited by '|' that contains the name of the apps with worker indexing disabled.
        /// </summary>
        public string WorkerIndexingDisabledApps
        {
            get
            {
                return GetFeature(RpcWorkerConstants.WorkerIndexingDisabledApps) ?? string.Empty;
            }
        }

        /// <summary>
        /// Gets a value indicating whether Linux Log Backoff is disabled in the hosting config.
        /// </summary>
        public bool DisableLinuxAppServiceLogBackoff
        {
            get
            {
                return GetFeature(ScriptConstants.HostingConfigDisableLinuxAppServiceExecutionEventLogBackoff) == "1";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether Linux Appservice/EP Detailed Execution Event is disabled in the hosting config.
        /// </summary>
        public bool DisableLinuxAppServiceExecutionDetails
        {
            get
            {
                return GetFeature(ScriptConstants.HostingConfigDisableLinuxAppServiceDetailedExecutionEvents) == "1";
            }

            set
            {
               _features[ScriptConstants.HostingConfigDisableLinuxAppServiceDetailedExecutionEvents] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets feature by name.
        /// </summary>
        /// <param name="name">Feature name.</param>
        /// <returns>String value from hostig configuration.</returns>
        public string GetFeature(string name)
        {
            if (_features.TryGetValue(name, out string value))
            {
                return value;
            }
            return null;
        }
    }
}
