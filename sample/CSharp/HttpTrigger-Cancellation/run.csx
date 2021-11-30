using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public static async Task<IActionResult> Run(HttpRequest req, ILogger log, CancellationToken token)
{
    log.LogInformation("C# HTTP trigger cancellation function processing a request.");

    try
    {
        await Task.Delay(10000, token);
        log.LogInformation("Function invocation successful.");
        return new OkResult();

    }
    catch (OperationCanceledException)
    {
        log.LogInformation("Function invocation cancelled.");
        return new NotFoundResult();
    }
    catch (Exception e)
    {
        return new StatusCodeResult(500);
    }
}
