#r "Newtonsoft.Json"

using System;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

public static void Run(string input, JObject item, TraceWriter log)
{
    item["Text"] = "This was updated!";

    log.Verbose($"Updating item {item["Id"]}");
}