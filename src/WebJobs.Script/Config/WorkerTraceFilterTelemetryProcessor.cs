// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class WorkerTraceFilterTelemetryProcessor : ITelemetryProcessor
    {
        private readonly ITelemetryProcessor _next;

        internal static readonly AsyncLocal<bool> FilterApplicationInsightsFromWorker = new();

        public WorkerTraceFilterTelemetryProcessor(ITelemetryProcessor next)
        {
            _next = next;
        }

        public void Process(ITelemetry item)
        {
            if (FilterApplicationInsightsFromWorker.Value)
            {
                return;
            }

            _next.Process(item);
        }
    }
}
