using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

public static void Run(WorkItem input, out string output, TraceWriter log)
{
    string json = string.Format("{{ \"id\": \"{0}\" }}", input.Id);

    log.Info($"C# script processed queue message. Item={json}");

    output = json;
}

public class WorkItem
{
    public string Id { get; set; }
}