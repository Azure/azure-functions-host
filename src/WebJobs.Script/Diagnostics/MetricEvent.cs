// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public abstract class MetricEvent
    {
        public string FunctionName { get; set; }

        public DateTime Timestamp { get; set; }

        public ValueStopwatch StopWatch { get; set; }

        public TimeSpan Duration { get; private set; }

        public string Data { get; set; }

        public string RuntimeSiteName { get; set; }

        public string SlotName { get; set; }

        public bool Completed { get; private set; }

        public void Complete()
        {
            if (StopWatch.IsActive)
            {
                Duration = StopWatch.GetElapsedTime();
            }
            else
            {
                Duration = DateTime.UtcNow - Timestamp;
            }

            Completed = true;
        }
    }
}
