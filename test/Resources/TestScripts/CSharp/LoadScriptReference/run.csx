#load "Class.csx"

using System.Net;
using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(HttpRequest req, ILogger log)
{
    string response = new Test().Response;
    log.LogInformation("response: " + response);
    return new OkObjectResult(response);
}