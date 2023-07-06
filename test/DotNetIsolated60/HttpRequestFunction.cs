using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DotNetIsolated60
{
    public class HttpRequestFunction
    {
        private readonly ILogger _logger;

        public HttpRequestFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<HttpRequestFunction>();
        }

        [Function(nameof(HttpRequestFunction))]
        public Task Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return req.HttpContext.Response.WriteAsync("Welcome to Azure Functions!");
        }
    }
}
