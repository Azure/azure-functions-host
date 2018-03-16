using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(HttpRequest req, ExecutionContext context)
{
    return new OkObjectResult(context);
}