#r "Newtonsoft.Json"

using System;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

public static void Run(QueueInput input, JObject item)
{
    item["text"] = "This was updated!";
}

public class QueueInput
{
    public string DocumentId { get; set; }
}