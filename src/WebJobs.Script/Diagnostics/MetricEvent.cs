// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public abstract class MetricEvent
    {
        public string FunctionName { get; set; }

        public DateTime Timestamp { get; set; }

        public TimeSpan Duration { get; set; }

        public string Data { get; set; }

        public string RuntimeSiteName { get; set; }
    }
}
