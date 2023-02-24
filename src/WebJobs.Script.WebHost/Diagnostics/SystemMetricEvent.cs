// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    [DebuggerDisplay("{DebugValue,nq}")]
    public class SystemMetricEvent : MetricEvent
    {
        // Within the MetricsEventManager we would like to use Interlock.Increment to increment
        // the Count when we queue an event. We cannot use the 'ref' keyword with a property so
        // in order to use Interlock.Increment with Count we have to introduce an internal backing field.
        #pragma warning disable SA1401 // FieldsMustBePrivate
        internal long _count;
        #pragma warning restore SA1401

        private string DebugValue
        {
            get
            {
                string key = string.Empty;
                if (!string.IsNullOrEmpty(FunctionName))
                {
                    key = $"Function: {FunctionName}, ";
                }
                key += $"Event: {EventName}, Count: {Count}";
                return $"({key})";
            }
        }

        public string EventName { get; set; }

        public long Average { get; set; }

        public long Minimum { get; set; }

        public long Maximum { get; set; }

        public long Count
        {
            get { return _count; }
            set { _count = value; }
        }
    }
}