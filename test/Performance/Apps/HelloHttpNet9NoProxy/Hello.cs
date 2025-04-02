using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace HelloHttpNet9
{
    public sealed class Hello(ILogger<Hello> logger)
    {
        [Function("HelloHttp")]
        public HttpResponseData Run(
#if AUTH_FUNCTION
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req
#else
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req
#endif
        )
        {
            logger.LogInformation("C# HTTP trigger function processed a request.");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            response.WriteString("Welcome to Azure Functions!");

            return response;
        }
    }
}
