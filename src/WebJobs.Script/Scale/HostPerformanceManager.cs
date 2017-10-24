// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public class HostPerformanceManager
    {
        internal const float ConnectionThreshold = 0.80F;
        internal const float ThreadThreshold = 0.80F;
        internal const float ProcessesThreshold = 0.80F;
        internal const float NamedPipesThreshold = 0.80F;
        internal const float SectionsThreshold = 0.80F;

        private readonly ScriptSettingsManager _settingsManager;

        // for mock testing
        public HostPerformanceManager()
        {
        }

        public HostPerformanceManager(ScriptSettingsManager settingsManager)
        {
            if (settingsManager == null)
            {
                throw new ArgumentNullException(nameof(settingsManager));
            }

            _settingsManager = settingsManager;
        }

        public virtual bool IsUnderHighLoad(Collection<string> exceededCounters = null, TraceWriter traceWriter = null)
        {
            var counters = GetPerformanceCounters(traceWriter);
            if (counters != null)
            {
                return IsUnderHighLoad(counters, exceededCounters);
            }

            return false;
        }

        internal static bool IsUnderHighLoad(ApplicationPerformanceCounters counters, Collection<string> exceededCounters = null)
        {
            bool exceeded = false;

            // determine all counters whose limits have been exceeded
            exceeded |= ThresholdExceeded("Connections", counters.Connections, counters.ConnectionLimit, ConnectionThreshold, exceededCounters);
            exceeded |= ThresholdExceeded("Threads", counters.Threads, counters.ThreadLimit, ThreadThreshold, exceededCounters);
            exceeded |= ThresholdExceeded("Processes", counters.Processes, counters.ProcessLimit, ProcessesThreshold, exceededCounters);
            exceeded |= ThresholdExceeded("NamedPipes", counters.NamedPipes, counters.NamedPipeLimit, NamedPipesThreshold, exceededCounters);
            exceeded |= ThresholdExceeded("Sections", counters.Sections, counters.SectionLimit, SectionsThreshold, exceededCounters);

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

        internal ApplicationPerformanceCounters GetPerformanceCounters(TraceWriter traceWriter = null)
        {
            // these performance counter thresholds are only present in
            // shared plans
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
                    traceWriter?.Error($"Failed to deserialize application performance counters. JSON Content: \"{json}\"", ex);
                }
            }

            return null;
        }
    }
}
