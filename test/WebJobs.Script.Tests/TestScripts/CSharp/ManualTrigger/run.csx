using System;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string input, TraceWriter log)
{
    log.Info(input);
}