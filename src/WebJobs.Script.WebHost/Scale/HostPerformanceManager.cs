// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Scale
{
    public class HostPerformanceManager
    {
        internal const float ConnectionThreshold = 0.80F;
        internal const float ThreadThreshold = 0.80F;
        internal const float ProcessesThreshold = 0.80F;
        internal const float NamedPipesThreshold = 0.80F;
        internal const float SectionsThreshold = 0.80F;

        private readonly ScriptSettingsManager _settingsManager;
        private readonly TraceWriter _traceWriter;

        // for mock testing
        public HostPerformanceManager()
        {
        }

        public HostPerformanceManager(ScriptSettingsManager settingsManager, TraceWriter traceWriter)
        {
            _settingsManager = settingsManager;
            _traceWriter = traceWriter;
        }

        public virtual bool IsUnderHighLoad(Collection<string> exceededCounters = null)
        {
            var counters = GetPerformanceCounters();
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

        internal ApplicationPerformanceCounters GetPerformanceCounters()
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
                    _traceWriter.Error($"Failed to deserialize application performance counters. JSON Content: \"{json}\"", ex);
                }
            }

            return null;
        }
    }
}
