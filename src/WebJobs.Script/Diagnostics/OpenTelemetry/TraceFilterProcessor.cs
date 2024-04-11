// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using OpenTelemetry;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    internal class TraceFilterProcessor : BaseProcessor<Activity>
    {
        private TraceFilterProcessor() { }

        public static TraceFilterProcessor Instance { get; } = new TraceFilterProcessor();

        public override void OnEnd(Activity activity)
        {
            string url = activity.GetTagItem("http.url") as string ?? activity.GetTagItem("url.full") as string;
            DropDependencyTracesToAppInsightsEndpoints(activity, url);

            DropDependencyTracesToHostStorageEndpoints(activity, url);

            DropDependencyTracesToHostLoopbackEndpoints(activity, url);
            base.OnEnd(activity);
        }

        private void DropDependencyTracesToHostLoopbackEndpoints(Activity activity, string url)
        {
            if (activity.ActivityTraceFlags is ActivityTraceFlags.Recorded)
            {
                if (url?.Contains("/AzureFunctionsRpcMessages.FunctionRpc/", StringComparison.OrdinalIgnoreCase) is true
                    || url?.EndsWith("/getScriptTag", StringComparison.OrdinalIgnoreCase) is true)
                {
                    activity.ActivityTraceFlags = ActivityTraceFlags.None;
                }
            }
        }

        private void DropDependencyTracesToAppInsightsEndpoints(Activity activity, string url)
        {
            if (activity.ActivityTraceFlags is ActivityTraceFlags.Recorded
                && activity.Source.Name is "Azure.Core.Http" or "System.Net.Http")
            {
                if (url?.Contains("applicationinsights.azure.com", StringComparison.OrdinalIgnoreCase) is true
                    || url?.Contains("rt.services.visualstudio.com/QuickPulseService.svc", StringComparison.OrdinalIgnoreCase) is true)
                {
                    // don't record all the HTTP calls to Live Stream aka QuickPulse
                    activity.ActivityTraceFlags = ActivityTraceFlags.None;
                }
            }
        }

        private void DropDependencyTracesToHostStorageEndpoints(Activity activity, string url)
        {
            if (activity.ActivityTraceFlags is ActivityTraceFlags.Recorded)
            {
                if (activity.Source.Name is "Azure.Core.Http" or "System.Net.Http"
                    && (activity.GetTagItem("az.namespace") as string) is "Microsoft.Storage")
                {
                    if (url?.Contains("/azure-webjobs-", System.StringComparison.OrdinalIgnoreCase) is true)
                    {
                        // don't record all the HTTP calls to backing storage used by the host
                        activity.ActivityTraceFlags = ActivityTraceFlags.None;
                    }
                }
            }
        }
    }
}
