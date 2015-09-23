// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Azure.WebJobs.Host;

namespace Host
{
    public static partial class Functions
    {
        public static void ScheduledJob(TimerInfo timerInfo, TraceWriter traceWriter)
        {
            traceWriter.Info(string.Format("C# ScheduledJob invoked at ", DateTime.Now));
        }
    }
}
