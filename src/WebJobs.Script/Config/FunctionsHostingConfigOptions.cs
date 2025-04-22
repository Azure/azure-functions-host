// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class FunctionsHostingConfigOptions
    {
        private readonly Dictionary<string, string> _features;
        private PathString[] _allowedInternalAuthApis;

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
        internal bool FunctionsWorkerDynamicConcurrencyEnabled
        {
            get
            {
                return GetFeature(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled) == "1";
            }
        }

        /// <summary>
        /// Gets a value indicating whether worker indexing feature is enabled in the hosting config.
        /// </summary>
        internal bool WorkerIndexingEnabled
        {
            get
            {
                return GetFeature(RpcWorkerConstants.WorkerIndexingEnabled) == "1";
            }
        }

        /// <summary>
        /// Gets or Sets a value indicating whether the host should shutdown webhost worker channels during shutdown.
        /// </summary>
        internal bool ShutdownWebhostWorkerChannelsOnHostShutdown
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(RpcWorkerConstants.ShutdownWebhostWorkerChannelsOnHostShutdown, true);
            }

            set
            {
                _features[RpcWorkerConstants.ShutdownWebhostWorkerChannelsOnHostShutdown] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether SWT tokens should be sent on outgoing requests.
        /// </summary>
        internal bool SwtIssuerEnabled
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(ScriptConstants.HostingConfigSwtIssuerEnabled, true);
            }

            set
            {
                _features[ScriptConstants.HostingConfigSwtIssuerEnabled] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to use cgroup memory metrics for reporting memory usage.
        /// </summary>
        internal bool IsCGroupMemoryMetricsEnabled
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(ScriptConstants.FeatureFlagEnableCGroupMemoryMetrics, false);
            }

            set
            {
                _features[ScriptConstants.FeatureFlagEnableCGroupMemoryMetrics] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets or sets a string delimited by '|' that contains a list of admin APIs that are allowed to
        /// be invoked internally by platform components.
        /// </summary>
        internal string InternalAuthApisAllowList
        {
            get
            {
                return GetFeature(ScriptConstants.HostingConfigInternalAuthApisAllowList);
            }

            set
            {
                _allowedInternalAuthApis = null;
                _features[ScriptConstants.HostingConfigInternalAuthApisAllowList] = value;
            }
        }

        /// <summary>
        /// Gets a string delimited by '|' that contains the name of the apps with worker indexing disabled.
        /// </summary>
        internal string WorkerIndexingDisabledApps
        {
            get
            {
                return GetFeature(RpcWorkerConstants.WorkerIndexingDisabledApps) ?? string.Empty;
            }
        }

        /// <summary>
        /// Gets a value indicating whether Linux Log Backoff is disabled in the hosting config.
        /// </summary>
        internal bool DisableLinuxAppServiceLogBackoff
        {
            get
            {
                return GetFeature(ScriptConstants.HostingConfigDisableLinuxAppServiceExecutionEventLogBackoff) == "1";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether Linux AppService/EP Detailed Execution Event is disabled in the hosting config.
        /// </summary>
        internal bool DisableLinuxAppServiceExecutionDetails
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

        internal bool EnableOrderedInvocationMessages
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(ScriptConstants.FeatureFlagEnableOrderedInvocationmessages, true);
            }

            set
            {
                _features[ScriptConstants.FeatureFlagEnableOrderedInvocationmessages] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore the TestData property during read and write operations of functions metadata.
        /// </summary>
        internal bool IsTestDataSuppressionEnabled
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(ScriptConstants.FeatureFlagEnableTestDataSuppression, false);
            }

            set
            {
                _features[ScriptConstants.FeatureFlagEnableTestDataSuppression] = value ? "1" : "0";
            }
        }

        /// <summary>
        /// Gets the highest version of extension bundle v3 supported.
        /// </summary>
        internal string MaximumBundleV3Version
        {
            get
            {
                return GetFeature(ScriptConstants.MaximumBundleV3Version);
            }
        }

        /// <summary>
        /// Gets the highest version of extension bundle v4 supported.
        /// </summary>
        internal string MaximumBundleV4Version
        {
            get
            {
                return GetFeature(ScriptConstants.MaximumBundleV4Version);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the host should revert the worker shutdown behavior in the WebHostWorkerChannelManager.
        /// </summary>
        internal bool RevertWorkerShutdownBehavior
        {
            get
            {
                return GetFeature(RpcWorkerConstants.RevertWorkerShutdownBehavior) == "1";
            }
        }

        internal bool ThrowOnMissingFunctionsWorkerRuntime
        {
            get
            {
                return GetFeature(RpcWorkerConstants.ThrowOnMissingFunctionsWorkerRuntime) == "1";
            }
        }

        internal bool WorkerRuntimeStrictValidationEnabled
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(RpcWorkerConstants.WorkerRuntimeStrictValidationEnabled, false);
            }
        }

        internal bool IsDotNetInProcDisabled
        {
            get
            {
                return GetFeatureAsBooleanOrDefault(ScriptConstants.HostingConfigDotNetInProcDisabled, false);
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

        /// <summary>
        /// Gets a feature by name and attempts to parse it into a boolean.
        /// Returns "True, False" or ints as booleans. Returns True for any non-zero integer.
        /// If none of those (or empty), returns default.
        /// </summary>
        /// <param name="name">Feature name.</param>
        /// <param name="defaultValue">The default value to return if the feature value cannot be parsed into a boolean.</param>
        /// <returns>Boolean value if parse-able from hosting configuration. Otherwise, the defaultValue.</returns>
        internal bool GetFeatureAsBooleanOrDefault(string name, bool defaultValue)
        {
            string featureValue = GetFeature(name);

            if (string.IsNullOrWhiteSpace(featureValue))
            {
                return defaultValue;
            }

            if (bool.TryParse(featureValue, out bool parsedBool))
            {
                return parsedBool;
            }

            if (int.TryParse(featureValue, out int parsedInt))
            {
                return parsedInt != 0;
            }
            return defaultValue;
        }

        internal bool CheckInternalAuthAllowList(HttpRequest httpRequest)
        {
            if (InternalAuthApisAllowList != null && _allowedInternalAuthApis == null)
            {
                // initialize our cached allow list on demand
                _allowedInternalAuthApis = InternalAuthApisAllowList.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(p => new PathString(p)).ToArray();
            }

            if (_allowedInternalAuthApis != null)
            {
                // An allow list is configured, so we ensure that the current request
                // matches any of the allowed APIs.
                foreach (PathString ps in _allowedInternalAuthApis)
                {
                    if (httpRequest.Path.StartsWithSegments(ps, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            // no allow list configured
            return true;
        }
    }
}