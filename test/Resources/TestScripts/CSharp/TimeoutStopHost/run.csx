using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string input, TextWriter writer, TraceWriter log)
{
    log.Info(input);

    while (true)
    {
        log.Info("Waiting");
        Thread.Sleep(100);
    }
}

public class Context
{
    public string Scenario { get; set; }
    public string Data { get; set; }
}