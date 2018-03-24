// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public class HostPerformanceManager
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly HostHealthMonitorConfiguration _healthMonitorConfig;

        // for mock testing
        public HostPerformanceManager()
        {
        }

        public HostPerformanceManager(ScriptSettingsManager settingsManager, HostHealthMonitorConfiguration healthMonitorConfig)
        {
            if (settingsManager == null)
            {
                throw new ArgumentNullException(nameof(settingsManager));
            }
            if (healthMonitorConfig == null)
            {
                throw new ArgumentNullException(nameof(healthMonitorConfig));
            }

            _settingsManager = settingsManager;
            _healthMonitorConfig = healthMonitorConfig;
        }

        public virtual bool IsUnderHighLoad(Collection<string> exceededCounters = null, ILogger logger = null)
        {
            var counters = GetPerformanceCounters(logger);
            if (counters != null)
            {
                return IsUnderHighLoad(counters, exceededCounters, _healthMonitorConfig.CounterThreshold);
            }

            return false;
        }

        internal static bool IsUnderHighLoad(ApplicationPerformanceCounters counters, Collection<string> exceededCounters = null, float threshold = HostHealthMonitorConfiguration.DefaultCounterThreshold)
        {
            bool exceeded = false;

            // determine all counters whose limits have been exceeded
            exceeded |= ThresholdExceeded("Connections", counters.Connections, counters.ConnectionLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Threads", counters.Threads, counters.ThreadLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Processes", counters.Processes, counters.ProcessLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("NamedPipes", counters.NamedPipes, counters.NamedPipeLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Sections", counters.Sections, counters.SectionLimit, threshold, exceededCounters);

            return exceeded;
        }

        internal static bool ThresholdExceeded(string name, long currentValue, long limit, float threshold, Collection<string> exceededCounters = null)
        {
            if (limit <= 0)
            {
                // no limit to apply
                return false;
            }

            float currentUsage = (float)currentValue / limit;
            bool exceeded = currentUsage > threshold;
            if (exceeded && exceededCounters != null)
            {
                exceededCounters.Add(name);
            }
            return exceeded;
        }

        internal ApplicationPerformanceCounters GetPerformanceCounters(ILogger logger = null)
        {
            string json = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteAppCountersName);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    // TEMP: need to parse this specially to work around bug where
                    // sometimes an extra garbage character occurs after the terminal
                    // brace
                    int idx = json.LastIndexOf('}');
                    if (idx > 0)
                    {
                        json = json.Substring(0, idx + 1);
                    }

                    return JsonConvert.DeserializeObject<ApplicationPerformanceCounters>(json);
                }
                catch (JsonReaderException ex)
                {
                    logger.LogError($"Failed to deserialize application performance counters. JSON Content: \"{json}\"", ex);
                }
            }

            return null;
        }
    }
}
