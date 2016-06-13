using System;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string input, out string output, TraceWriter log)
{
    log.Info($"C# ApiHub trigger function processed a file...");

    output = input;
}
