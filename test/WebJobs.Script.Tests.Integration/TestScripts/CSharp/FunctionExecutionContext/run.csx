using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(HttpRequest req, ExecutionContext context)
{
    req.HttpContext.Items["ContextValue"] = context;

    return new OkObjectResult(string.Empty);
}