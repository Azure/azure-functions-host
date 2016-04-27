using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string input, out string output, TraceWriter log)
{
    log.Info($"C# ApiHub trigger function processed a file...");

    output = input;
}