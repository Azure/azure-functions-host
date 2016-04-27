using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string blob, out string output, TraceWriter log)
{
    log.Info($"C# Blob trigger function processed a blob. Blob={blob}");

    output = blob;
}