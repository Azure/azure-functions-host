using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public static IActionResult Run(HttpRequest req, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    if (req.Query.TryGetValue("name", out StringValues value))
    {
        return new OkObjectResult($"Hello {value.ToString()}");
    }

    return new BadRequestObjectResult("Please pass a name on the query string or in the request body");
}