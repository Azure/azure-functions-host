// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    [DebuggerDisplay("{EventName} {Count}")]
    public class SystemMetricEvent : MetricEvent
    {
        public string EventName { get; set; }

        public long Average { get; set; }

        public long Minimum { get; set; }

        public long Maximum { get; set; }

        public long Count { get; set; }
    }
}
