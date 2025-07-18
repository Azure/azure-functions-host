using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DotNetIsolatedUnsupportedWorker
{
    public class HttpRequestFunction
    {
        private readonly ILogger<HttpRequestFunction> _logger;

        public HttpRequestFunction(ILogger<HttpRequestFunction> logger)
        {
            _logger = logger;
        }

        [Function(nameof(HttpRequestFunction))]
        public Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return req.HttpContext.Response.WriteAsync("Welcome to Azure Functions!");
        }
    }
}
