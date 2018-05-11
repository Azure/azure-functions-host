#r "Microsoft.AspNetCore.Http.Extensions"

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;

public static IActionResult Run(HttpRequest req, TraceWriter log)
{
    return new OkObjectResult(req.GetDisplayUrl());
}