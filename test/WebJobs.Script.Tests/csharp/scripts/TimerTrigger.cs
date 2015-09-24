// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Extensions.Timers;

namespace WebJobs.Script.Tests
{
    public static partial class Functions
    {
        public static int InvokeCount { get; set; }

        public static void TimerTrigger(TimerInfo timerInfo)
        {
            File.AppendAllText("joblog.txt", DateTime.Now.ToString() + "\r\n");
            InvokeCount++;
        }
    }
}
