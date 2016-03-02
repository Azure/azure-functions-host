using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

public static void Run(Stream input, TraceWriter log)
{
    log.Verbose($"CSharp Blob trigger function processed a work item. Item={input}");
}