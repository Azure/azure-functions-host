#r "..\SharedAssemblies\PrimaryDependency.dll"

using PrimaryDependency;
using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(HttpRequest req, TraceWriter log)
{
    return new OkObjectResult(new Primary().GetValue());
}