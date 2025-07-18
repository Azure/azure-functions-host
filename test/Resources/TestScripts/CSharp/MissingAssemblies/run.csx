#r "MissingDependency.dll"

using MissingDependency;
using Microsoft.AspNetCore.Mvc;

/// <summary>
///  This is a function to test that the correct warning and link to documentation gets sent over.
/// </summary>
/// <param name="req"></param>
/// <param name="log"></param>
/// <returns></returns>
public static IActionResult Run(HttpRequest req, TraceWriter log)
{
    return new OkObjectResult(new Primary().GetValue());
}