// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace Host
{
    public static partial class Functions
    {
        public static void TimerTrigger(TimerInfo timerInfo, TraceWriter trace)
        {
            trace.Info(string.Format("C# TimerTrigger function invoked at ", DateTime.Now));
        }
    }
}
