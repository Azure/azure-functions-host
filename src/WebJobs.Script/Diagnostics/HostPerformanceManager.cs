// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class HostPerformanceManager
    {
        internal const float ConnectionThreshold = 0.80F;
        internal const float ThreadThreshold = 0.80F;
        internal const float ProcessesThreshold = 0.80F;
        internal const float NamedPipesThreshold = 0.80F;

        private readonly ScriptSettingsManager _settingsManager;

        public HostPerformanceManager(ScriptSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        public bool IsUnderHighLoad()
        {
            var counters = GetPerformanceCounters();
            if (counters != null)
            {
                return IsUnderHighLoad(counters);
            }

            return false;
        }

        internal static bool IsUnderHighLoad(ApplicationPerformanceCounters counters)
        {
            return
                ThresholdExceeded(counters.Connections, counters.ConnectionLimit, ConnectionThreshold) ||
                ThresholdExceeded(counters.Threads, counters.ThreadLimit, ThreadThreshold) ||
                ThresholdExceeded(counters.Processes, counters.ProcessLimit, ProcessesThreshold) ||
                ThresholdExceeded(counters.NamedPipes, counters.NamedPipeLimit, NamedPipesThreshold);
        }

        internal static bool ThresholdExceeded(int currentValue, int limit, float threshold)
        {
            float currentUsage = (float)currentValue / limit;
            return currentUsage > threshold;
        }

        internal ApplicationPerformanceCounters GetPerformanceCounters()
        {
            string json = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteAppCountersName);
            if (!string.IsNullOrEmpty(json))
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

            return null;
        }
    }
}
