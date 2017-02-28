#r "Newtonsoft.Json"

using System;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static void Run(string input, TraceWriter log)
{
    dynamic inputObject = JsonConvert.DeserializeObject(input);

    log.Info($"Input={input}");
}