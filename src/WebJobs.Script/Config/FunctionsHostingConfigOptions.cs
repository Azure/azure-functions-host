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
        /// Gets a value indicating whether worker concurrency feature is enabled in the hosting config.
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
        /// Gets or sets a value indicating whether SWT tokens should be accepted.
        /// </summary>
        public bool SwtAuthenticationEnabled
        {
            get
            {
                return GetFeatureOrDefault(ScriptConstants.HostingConfigSwtAuthenticationEnabled, "1") == "1";
            }

            set
            {
                _features[ScriptConstants.HostingConfigSwtAuthenticationEnabled] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether SWT tokens should be sent on outgoing requests.
        /// </summary>
        public bool SwtIssuerEnabled
        {
            get
            {
                return GetFeatureOrDefault(ScriptConstants.HostingConfigSwtIssuerEnabled, "1") == "1";
            }

            set
            {
                _features[ScriptConstants.HostingConfigSwtIssuerEnabled] = value ? "1" : "0";
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
        /// Gets or sets a value indicating whether Linux AppService/EP Detailed Execution Event is disabled in the hosting config.
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

        public bool EnableOrderedInvocationMessages
        {
            get
            {
                return GetFeature(ScriptConstants.FeatureFlagEnableOrderedInvocationmessages) == "1";
            }

            set
            {
                _features[ScriptConstants.FeatureFlagEnableOrderedInvocationmessages] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets the highest version of extension bundle v3 supported.
        /// </summary>
        public string MaximumBundleV3Version
        {
            get
            {
                return GetFeature(ScriptConstants.MaximumBundleV3Version);
            }
        }

        /// <summary>
        /// Gets the highest version of extension bundle v4 supported.
        /// </summary>
        public string MaximumBundleV4Version
        {
            get
            {
                return GetFeature(ScriptConstants.MaximumBundleV4Version);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the host should revert the worker shutdown behavior in the WebHostWorkerChannelManager.
        /// </summary>
        public bool RevertWorkerShutdownBehavior
        {
            get
            {
                return GetFeature(RpcWorkerConstants.RevertWorkerShutdownBehavior) == "1";
            }
        }

        public bool ThrowOnMissingFunctionsWorkerRuntime
        {
            get
            {
                return GetFeature(RpcWorkerConstants.ThrowOnMissingFunctionsWorkerRuntime) == "1";
            }
        }

        /// <summary>
        /// Gets feature by name.
        /// </summary>
        /// <param name="name">Feature name.</param>
        /// <returns>String value from hosting configuration.</returns>
        public string GetFeature(string name)
        {
            if (_features.TryGetValue(name, out string value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Gets a feature by name, returning the specified default value if not found.
        /// </summary>
        /// <param name="name">Feature name.</param>
        /// <param name="defaultValue">The default value to use.</param>
        /// <returns>String value from hosting configuration.</returns>
        public string GetFeatureOrDefault(string name, string defaultValue)
        {
            return GetFeature(name) ?? defaultValue;
        }
    }
}
