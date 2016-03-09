using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

public static void Run(TimerInfo input, out string message, TraceWriter log)
{
    log.Verbose("CSharp Timer trigger function executed.");

    message = $"Trigger function executed at {DateTime.Now}";
}