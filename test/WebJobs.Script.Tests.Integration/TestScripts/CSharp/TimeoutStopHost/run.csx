using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string input, TextWriter writer, ILogger log)
{
    log.LogInformation(input);

    while (true)
    {
        log.LogInformation("Waiting");
        Thread.Sleep(100);
    }
}

public class Context
{
    public string Scenario { get; set; }
    public string Data { get; set; }
}