using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

public static void Run(TimerInfo timerInfo, TraceWriter log)
{
    log.Info("C# Timer trigger function executed.");
}