using System;
using Microsoft.Azure.WebJobs.Host;

public static string Run(string input, TraceWriter log)
{
    log.Info($"C# ApiHub trigger function processed a file...");
    return input;
}