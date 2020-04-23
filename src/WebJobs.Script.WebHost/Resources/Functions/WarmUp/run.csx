#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Storage"

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

public static async Task<IActionResult> Run(HttpRequest req, ILogger log)
{
    // Code in this function is to just JIT dynamic types and initialize static objects, ... so we don't pay the cost during specialization.
    log.LogInformation("WarmUp function invoked.");

    string name = req.Query["name"];

    string requestBody = await (new StreamReader(req.Body)).ReadToEndAsync();
    dynamic data = JsonConvert.DeserializeObject(requestBody);
    name = name ?? data?.name ?? "";

    // This is just to warmup storage calls in placeholder process to improve cold startup time.
    CloudBlockBlob output = new CloudBlockBlob(new Uri("https://functionswarmup.blob.core.windows.net/warmup/warmup"));

    try
    {
        Task blobTask = output.DownloadTextAsync();
        await Task.WhenAny(blobTask, Task.Delay(TimeSpan.FromSeconds(5)));
    }
    catch
    {
    }

    HashSet<Guid> hashSet = new HashSet<Guid>();
    hashSet.Add(Guid.NewGuid());
    hashSet.Add(Guid.NewGuid());

    foreach (var item in hashSet)
    {
        int i = item.GetHashCode();
    }

    return new OkObjectResult("WarmUp complete.");
}