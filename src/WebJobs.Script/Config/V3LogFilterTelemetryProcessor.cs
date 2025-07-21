// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Telemetry processor that filters V3 logs for Kusto while preserving Application Insights logging.
    /// This processor should only block telemetry that is specifically destined for Kusto/Linux event generation,
    /// while allowing all telemetry to continue flowing to Application Insights.
    /// </summary>
    internal class V3LogFilterTelemetryProcessor : ITelemetryProcessor
    {
        private readonly ITelemetryProcessor _next;
        private readonly IOptionsMonitor<FunctionsHostingConfigOptions> _hostingConfigOptions;

        internal static readonly AsyncLocal<bool> FilterV3LogsForKusto = new();

        public V3LogFilterTelemetryProcessor(ITelemetryProcessor next, IOptionsMonitor<FunctionsHostingConfigOptions> hostingConfigOptions)
        {
            _next = next;
            _hostingConfigOptions = hostingConfigOptions;
        }

        public void Process(ITelemetry item)
        {
            // Only filter if V3 logs are disabled AND this is specifically a Kusto-destined log
            if (_hostingConfigOptions.CurrentValue.DisableV3Logs && FilterV3LogsForKusto.Value)
            {
                // Block this telemetry from reaching Kusto/Linux event generation
                return;
            }

            // Allow all other telemetry to flow through to Application Insights
            _next.Process(item);
        }
    }
}