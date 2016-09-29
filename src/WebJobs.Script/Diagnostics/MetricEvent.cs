// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public abstract class MetricEvent
    {
        public DateTime Timestamp { get; set; }

        public TimeSpan Duration { get; set; }
    }
}
