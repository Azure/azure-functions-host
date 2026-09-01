using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DotNetIsolatedStartupTimeout;

public static class IisStartupRecovery
{
    [Function(nameof(IisStartupRecovery))]
    public static HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData request)
    {
        HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
        response.WriteString("Recovered");

        return response;
    }
}
