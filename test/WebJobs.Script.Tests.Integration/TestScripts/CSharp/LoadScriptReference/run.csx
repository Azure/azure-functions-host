#load "Class.csx"

using System.Net;
using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(HttpRequest req)
{
    string response = new Test().Response;
    req.HttpContext.Items["LoadedScriptResponse"] = response;

    return new OkObjectResult(response);
}