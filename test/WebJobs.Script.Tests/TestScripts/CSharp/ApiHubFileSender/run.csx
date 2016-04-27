
using System;

public static void Run(string input, out string item, TraceWriter log)
{
    log.Info($"C# ApiHub trigger function processed a file...");

    item = input;
}

