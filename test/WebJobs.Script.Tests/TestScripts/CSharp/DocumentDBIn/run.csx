#r "Newtonsoft.Json"

using System;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

public static void Run(string input, JObject item, TraceWriter log)
{
    item["text"] = "This was updated!";
}