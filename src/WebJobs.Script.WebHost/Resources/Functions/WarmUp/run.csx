#r "Newtonsoft.Json"

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

public static async Task<IActionResult> Run(HttpRequest req, ILogger log)
{
    // Code in this function is to just JIT dynamic types and initialize static objects, ... so we don't pay the cost during specialization.
    log.LogInformation("WarmUp function invoked.");

    string name = req.Query["name"];

    string requestBody = await (new StreamReader(req.Body)).ReadToEndAsync();
    dynamic data = JsonConvert.DeserializeObject(requestBody);
    name = name ?? data?.name ?? "";

    
    HashSet<Guid> hashSet = new HashSet<Guid>();
    hashSet.Add(Guid.NewGuid());
    hashSet.Add(Guid.NewGuid());

    foreach (var item in hashSet)
    {
        int i = item.GetHashCode();
    }

    return new OkObjectResult("WarmUp complete.");
}