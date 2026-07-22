using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DotNetIsolated60
{
    public class ReservedRouteCatchAll
    {
        [Function(nameof(ReservedRouteCatchAll))]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{*path}")] HttpRequestData request)
        {
            HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync("customer catch-all");

            return response;
        }
    }
}
