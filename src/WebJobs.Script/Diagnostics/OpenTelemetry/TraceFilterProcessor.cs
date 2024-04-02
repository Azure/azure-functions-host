// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal class TraceFilterProcessor : BaseProcessor<Activity>
    {
        private TraceFilterProcessor() { }

        public static TraceFilterProcessor Instance { get; } = new TraceFilterProcessor();

        public override void OnEnd(Activity data)
        {
            var dataTags = data.Tags.ToImmutableDictionary();

            DropDependencyTracesToAppInsightsEndpoints(data, dataTags);

            DropDependencyTracesToHostStorageEndpoints(data, dataTags);

            DropDependencyTracesToHostLoopbackEndpoints(data, dataTags);
            if (data.ActivityTraceFlags != ActivityTraceFlags.None)
            {
                Console.WriteLine(data.DisplayName);
            }
            Console.WriteLine();
            base.OnEnd(data);
        }

        private void DropDependencyTracesToHostLoopbackEndpoints(Activity data, IImmutableDictionary<string, string> dataTags)
        {
            if (data.ActivityTraceFlags is ActivityTraceFlags.Recorded)
            {
                var url = GetUrlTagValue(dataTags);
                if (url?.Contains("/AzureFunctionsRpcMessages.FunctionRpc/", System.StringComparison.OrdinalIgnoreCase) is true
                    || url?.EndsWith("/getScriptTag", System.StringComparison.OrdinalIgnoreCase) is true)
                {
                    data.ActivityTraceFlags = ActivityTraceFlags.None;
                }
            }
        }

        private string GetUrlTagValue(IImmutableDictionary<string, string> dataTags)
        {
            string url;
            _ = dataTags.TryGetValue("url.full", out url) || dataTags.TryGetValue("http.url", out url);
            return url;
        }

        private void DropDependencyTracesToAppInsightsEndpoints(Activity data, IImmutableDictionary<string, string> dataTags)
        {
            if (data.ActivityTraceFlags is ActivityTraceFlags.Recorded
                && data.Source.Name is "Azure.Core.Http" or "System.Net.Http")
            {
                string url = GetUrlTagValue(dataTags);
                if (url?.Contains("applicationinsights.azure.com", System.StringComparison.OrdinalIgnoreCase) is true
                    || url?.Contains("rt.services.visualstudio.com/QuickPulseService.svc", System.StringComparison.OrdinalIgnoreCase) is true)
                {
                    // don't record all the HTTP calls to Live Stream aka QuickPulse
                    data.ActivityTraceFlags = ActivityTraceFlags.None;
                }
            }
        }

        private void DropDependencyTracesToHostStorageEndpoints(Activity data, IImmutableDictionary<string, string> dataTags)
        {
            if (data.ActivityTraceFlags is ActivityTraceFlags.Recorded)
            {
                if (data.Source.Name is "Azure.Core.Http" or "System.Net.Http"
                    && dataTags.TryGetValue("az.namespace", out string azNamespace)
                    && azNamespace is "Microsoft.Storage")
                {
                    string url = GetUrlTagValue(dataTags);
                    if (url?.Contains("/azure-webjobs-", System.StringComparison.OrdinalIgnoreCase) is true)
                    {
                        // don't record all the HTTP calls to backing storage used by the host
                        data.ActivityTraceFlags = ActivityTraceFlags.None;
                    }
                }
            }
        }
    }
}
