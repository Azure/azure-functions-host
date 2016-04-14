
using System;

public static void Run(string input, out string item, TraceWriter log)
{
    log.Verbose($"C# ApiHub trigger function processed a file...");

    item = input;
}

