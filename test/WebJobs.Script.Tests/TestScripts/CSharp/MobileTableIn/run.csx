#r "Newtonsoft.Json"

using System;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

public static void Run(QueueInput input, JObject item, TraceWriter log)
{
    item["Text"] = "This was updated!";

    log.Info($"Updating item {item["Id"]}");
}

public class QueueInput
{
    public string RecordId { get; set; }
}