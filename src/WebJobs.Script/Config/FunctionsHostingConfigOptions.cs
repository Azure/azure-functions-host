// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class FunctionsHostingConfigOptions
    {
        private readonly Dictionary<string, string> _features;

        public FunctionsHostingConfigOptions()
        {
            _features = new Dictionary<string, string>();
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
        /// Gets the highest version of extension bundle v3 supported
        /// </summary>
        public string MaximumSupportedBundleV3Version
        {
            get
            {
                return GetFeature(ScriptConstants.MaximumSupportedBundleV3Version) ?? "3.19.0";
            }
        }

        /// <summary>
        /// Gets the highest version of extension bundle v4 supported
        /// </summary>
        public string MaximumSupportedBundleV4Version
        {
            get
            {
                return GetFeature(ScriptConstants.MaximumSupportedBundleV4Version) ?? "4.2.0";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether SWT tokens should be accepted.
        /// </summary>
        public bool SwtAuthenticationEnabled
        {
            get
            {
                return GetFeatureOrDefault(ScriptConstants.HostingConfigSwtAuthenticationEnabled, "0") == "1";
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
        /// Gets or sets a value indicating whether non-critical logs should be disabled in the host.
        /// If disabled, the host logs will be restricted meaning only critical logs will be written.
        /// </summary>
        public bool RestrictHostLogs
        {
            get
            {
                return GetFeatureOrDefault(ScriptConstants.HostingConfigRestrictHostLogs, "1") == "1";
            }

            set
            {
                _features[ScriptConstants.HostingConfigRestrictHostLogs] = value ? "1" : "0";
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

        /// <summary>
        /// Gets a feature by name, returning the specified default value if not found.
        /// </summary>
        /// <param name="name">Feature name.</param>
        /// <param name="defaultValue">The default value to use.</param>
        /// <returns>String value from hostig configuration.</returns>
        public string GetFeatureOrDefault(string name, string defaultValue)
        {
            return GetFeature(name) ?? defaultValue;
        }
    }
}
