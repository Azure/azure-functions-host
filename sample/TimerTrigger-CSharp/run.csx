#r "System"

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;

public static void Run(TimerInfo timerInfo, TextReader inputMessage, out string message, TraceWriter log)
{
    log.Verbose("CSharp Timer trigger function executed.");

    message = $"Trigger function executed at {DateTime.Now}";
}


