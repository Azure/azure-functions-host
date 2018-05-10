using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(HttpRequest req, TraceWriter log)
{
    log.Info("WarmUp function invoked.");

    return (ActionResult)new OkObjectResult("WarmUp complete.");

}