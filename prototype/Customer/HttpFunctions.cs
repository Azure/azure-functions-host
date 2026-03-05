using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Customer;

public class HttpFunctions
{
    private static int _requestCount;

    private readonly ILogger<HttpFunctions> _logger;

    public HttpFunctions(ILogger<HttpFunctions> logger)
    {
        _logger = logger;
    }

    [Function("Hello")]
    public IActionResult Hello(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "hello")] HttpRequest req)
    {
        var count = Interlocked.Increment(ref _requestCount);
        var pid = Environment.ProcessId;

        _logger.LogInformation("Hello function processed request #{Count} on PID {Pid}.", count, pid);

        var name = req.Query["name"].FirstOrDefault() ?? "World";

        return new OkObjectResult($"Hello, {name}! [PID={pid}, RequestCount={count}]");
    }
}
