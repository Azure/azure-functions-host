using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DotNetIsolated60
{
    public class HttpRequestDataFunction
    {
        private readonly ILogger<HttpRequestDataFunction> _logger;

        public HttpRequestDataFunction(ILogger<HttpRequestDataFunction> logger)
        {
            _logger = logger;
        }

        [Function(nameof(HttpRequestDataFunction))]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            response.WriteString("Welcome to Azure Functions!");

            return response;
        }
    }
}
